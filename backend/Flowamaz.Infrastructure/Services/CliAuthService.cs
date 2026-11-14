using System.Security.Cryptography;
using System.Text.Json;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Redis-backed CLI device flow. A device code (CLI polls it) and a user code (shown in the browser)
/// are stored with a 10-minute TTL; the user-code key indexes back to the device code so the browser
/// approval can find the pending flow. Approval records only who approved — no token is stored in
/// Redis — and the poll hands that approval to the API exactly once, which then creates a CLI session.
/// </summary>
public sealed class CliAuthService : ICliAuthService
{
    private static readonly TimeSpan FlowTtl = TimeSpan.FromMinutes(10);
    private const int IntervalSeconds = 2;
    private const int MaxClientNameLength = 100;

    private readonly IConnectionMultiplexer _redis;
    private readonly string _appBaseUrl;
    private readonly ILogger<CliAuthService> _logger;

    public CliAuthService(IConnectionMultiplexer redis, IConfiguration configuration, ILogger<CliAuthService> logger)
    {
        _redis = redis;
        _logger = logger;
        var configured = configuration["Platform:BaseUrl"];
        _appBaseUrl = string.IsNullOrWhiteSpace(configured) ? "https://app.flowamaz.io" : configured.TrimEnd('/');
    }

    private static string DeviceKey(string deviceCode) => $"cli:device:{deviceCode}";
    private static string UserCodeKey(string userCode) => $"cli:usercode:{userCode}";

    public async Task<CliDeviceAuthorization> InitiateDeviceFlowAsync(string? clientName = null, CancellationToken ct = default)
    {
        _logger.LogDebug("CliAuthService.InitiateDeviceFlowAsync enter");
        try
        {
            var deviceCode = RandomToken(32);
            var userCode = RandomUserCode();
            var db = _redis.GetDatabase();

            var state = JsonSerializer.Serialize(new DeviceState("pending", null, CleanClientName(clientName)));
            await db.StringSetAsync(DeviceKey(deviceCode), state, FlowTtl);
            await db.StringSetAsync(UserCodeKey(userCode), deviceCode, FlowTtl);

            var verificationUri = $"{_appBaseUrl}/cli-auth";
            var result = new CliDeviceAuthorization(
                deviceCode,
                userCode,
                verificationUri,
                $"{verificationUri}?code={userCode}",
                IntervalSeconds,
                (int)FlowTtl.TotalSeconds);

            _logger.LogInformation("CliAuthService.InitiateDeviceFlowAsync exit userCode={UserCode}", userCode);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CliAuthService.InitiateDeviceFlowAsync error");
            throw;
        }
    }

    public async Task<string?> GetPendingClientNameAsync(string userCode, CancellationToken ct = default)
    {
        _logger.LogDebug("CliAuthService.GetPendingClientNameAsync enter userCode={UserCode}", userCode);
        try
        {
            var state = await ReadPendingAsync(userCode);
            _logger.LogDebug("CliAuthService.GetPendingClientNameAsync exit found={Found}", state is not null);
            return state?.State.ClientName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CliAuthService.GetPendingClientNameAsync error userCode={UserCode}", userCode);
            throw;
        }
    }

    public async Task<bool> ApproveAsync(string userCode, Guid orgUserId, CancellationToken ct = default)
    {
        _logger.LogDebug("CliAuthService.ApproveAsync enter userCode={UserCode}", userCode);
        try
        {
            var pending = await ReadPendingAsync(userCode);
            if (pending is null)
            {
                _logger.LogWarning("CliAuthService.ApproveAsync unknown/expired/used userCode={UserCode}", userCode);
                return false;
            }

            var db = _redis.GetDatabase();
            var approved = JsonSerializer.Serialize(new DeviceState("approved", orgUserId, pending.Value.State.ClientName));
            // Keep the flow's original expiry; the code can't be approved a second time (by anyone).
            var ttl = await db.KeyTimeToLiveAsync(DeviceKey(pending.Value.DeviceCode)) ?? FlowTtl;
            await db.StringSetAsync(DeviceKey(pending.Value.DeviceCode), approved, ttl);
            await db.KeyDeleteAsync(UserCodeKey(userCode));
            _logger.LogInformation("CliAuthService.ApproveAsync exit approved userCode={UserCode} userId={UserId}", userCode, orgUserId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CliAuthService.ApproveAsync error userCode={UserCode}", userCode);
            throw;
        }
    }

    public async Task<CliTokenResult> PollTokenAsync(string deviceCode, CancellationToken ct = default)
    {
        _logger.LogDebug("CliAuthService.PollTokenAsync enter");
        try
        {
            var db = _redis.GetDatabase();
            var raw = await db.StringGetAsync(DeviceKey(deviceCode));
            if (raw.IsNullOrEmpty) return CliTokenResult.Waiting(); // unknown or expired → keep CLI polite

            var state = JsonSerializer.Deserialize<DeviceState>(raw.ToString());
            if (state is null || state.Status != "approved" || state.ApprovedBy is not { } userId)
                return CliTokenResult.Waiting();

            // One-time hand-off: only the poll that deletes the key gets the approval, so it can't be collected twice.
            if (!await db.KeyDeleteAsync(DeviceKey(deviceCode))) return CliTokenResult.Waiting();
            _logger.LogInformation("CliAuthService.PollTokenAsync exit approval delivered userId={UserId}", userId);
            return CliTokenResult.Approved(userId, state.ClientName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CliAuthService.PollTokenAsync error");
            throw;
        }
    }

    private async Task<(string DeviceCode, DeviceState State)?> ReadPendingAsync(string userCode)
    {
        var db = _redis.GetDatabase();
        var deviceCode = await db.StringGetAsync(UserCodeKey(userCode));
        if (deviceCode.IsNullOrEmpty) return null;
        var raw = await db.StringGetAsync(DeviceKey(deviceCode!));
        if (raw.IsNullOrEmpty) return null;
        var state = JsonSerializer.Deserialize<DeviceState>(raw.ToString());
        return state is { Status: "pending" } ? (deviceCode.ToString(), state) : null;
    }

    private static string? CleanClientName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var printable = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (printable.Length == 0) return null;
        return printable.Length > MaxClientNameLength ? printable[..MaxClientNameLength] : printable;
    }

    private static string RandomToken(int bytes) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    private static string RandomUserCode()
    {
        // Human-friendly: 8 chars from an unambiguous alphabet (no 0/O/1/I), grouped XXXX-XXXX.
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = new char[9];
        for (var i = 0; i < 9; i++)
            chars[i] = i == 4 ? '-' : alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }

    private sealed record DeviceState(string Status, Guid? ApprovedBy, string? ClientName);
}
