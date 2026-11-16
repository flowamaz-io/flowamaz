using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Gates;

/// <summary>
/// HMAC-SHA256 helpers for signing and verifying human-gate email click-through links.
/// The message format is "{gateDecisionId}:{action}:{expiresUnixSeconds}".
/// </summary>
public static class GateHmacHelper
{
    // Process-scoped random key used when GATE_SIGNING_KEY is absent in Development.
    // Shared across all callers in the same process so signed links validate correctly within one run.
    private static readonly Lazy<string> _devEphemeralKey = new(() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());

    /// <summary>
    /// Returns GATE_SIGNING_KEY from config, or a random ephemeral key in Development (with a warning).
    /// Call sites in Production never reach the ephemeral path because Program.cs startup validation
    /// throws if GATE_SIGNING_KEY is not set.
    /// </summary>
    public static string GetSigningKey(IConfiguration config, ILogger? logger = null)
    {
        var key = config["GATE_SIGNING_KEY"];
        if (!string.IsNullOrEmpty(key))
            return key;

        logger?.LogWarning(
            "GATE_SIGNING_KEY is not configured — using an ephemeral Development key. " +
            "Email gate links will be invalid after process restart. Set GATE_SIGNING_KEY for stable links.");
        return _devEphemeralKey.Value;
    }

    /// <summary>
    /// Builds an HMAC-SHA256 hex signature for <paramref name="message"/> using
    /// <paramref name="key"/>.
    /// </summary>
    public static string BuildHmac(string message, string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var hash = HMACSHA256.HashData(keyBytes, messageBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Validates <paramref name="sig"/> against the expected HMAC for <paramref name="message"/>
    /// using a constant-time comparison to prevent timing attacks.
    /// </summary>
    public static bool ValidateHmac(string message, string sig, string key)
    {
        var expected = BuildHmac(message, key);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(sig);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
