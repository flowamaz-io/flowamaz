using System.Security.Cryptography;
using System.Text;

namespace Flowamaz.Application.Workflow.Gates;

/// <summary>
/// HMAC-SHA256 helpers for signing and verifying human-gate email click-through links.
/// The message format is "{gateDecisionId}:{action}:{expiresUnixSeconds}".
/// </summary>
public static class GateHmacHelper
{
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
