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
/// approval can find the pending flow. The approving user's own access token is bound to the device
/// code and handed to the CLI exactly once.
/// </summary>
public sealed class CliAuthService : ICliAuthService
{
    private static readonly TimeSpan FlowTtl = TimeSpan.FromMinutes(10);
    private const int IntervalSeconds = 2;

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

    public async Task<CliDeviceAuthorization> InitiateDeviceFlowAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("CliAuthService.InitiateDeviceFlowAsync enter");
        try
        {
            var deviceCode = RandomToken(32);
            var userCode = RandomUserCode();
            var db = _redis.GetDatabase();

            var state = JsonSerializer.Serialize(new DeviceState("pending", null, null));
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

    public async Task<bool> ApproveAsync(string userCode, string accessToken, DateTime expiresAt, CancellationToken ct = default)
    {
        _logger.LogDebug("CliAuthService.ApproveAsync enter userCode={UserCode}", userCode);
        try
        {
            var db = _redis.GetDatabase();
            var deviceCode = await db.StringGetAsync(UserCodeKey(userCode));
            if (deviceCode.IsNullOrEmpty)
            {
                _logger.LogWarning("CliAuthService.ApproveAsync unknown/expired userCode={UserCode}", userCode);
                return false;
            }

            var state = JsonSerializer.Serialize(new DeviceState("approved", accessToken, expiresAt));
            await db.StringSetAsync(DeviceKey(deviceCode!), state, FlowTtl);
            _logger.LogInformation("CliAuthService.ApproveAsync exit approved userCode={UserCode}", userCode);
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
            if (state is null || state.Status != "approved" || string.IsNullOrEmpty(state.AccessToken))
                return CliTokenResult.Waiting();

            // One-time hand-off: delete the flow so the token can't be collected twice.
            await db.KeyDeleteAsync(DeviceKey(deviceCode));
            _logger.LogInformation("CliAuthService.PollTokenAsync exit token delivered");
            return CliTokenResult.Ready(state.AccessToken, state.ExpiresAt ?? DateTime.UtcNow.AddMinutes(15));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CliAuthService.PollTokenAsync error");
            throw;
        }
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

    private sealed record DeviceState(string Status, string? AccessToken, DateTime? ExpiresAt);
}
