using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Flowamaz.Core.Models;

/// <summary>
/// One connector operation sent from the API to the sandboxed connector runner (FUNCTIONAL.md §9.5).
/// Carries only what the operation needs: the node's interpolated inputs (input scoping) and the one
/// decrypted credential for this call. Never the workflow context or other secrets.
/// </summary>
public sealed record ConnectorRunRequest(
    string ConnectorId,
    string OperationId,
    JsonElement Input,
    ConnectorRunCredential? Credential,
    Guid WorkspaceId,
    Guid? InstanceId,
    string? NodeId,
    int TimeoutSeconds,
    IReadOnlyList<string>? AllowedDomains);

/// <summary>Credential as transmitted to the runner; <see cref="ToString"/> is redacted.</summary>
public sealed record ConnectorRunCredential(Guid Id, string Name, string AuthType, string Secret)
{
    public override string ToString() => $"ConnectorRunCredential {{ Id = {Id}, Name = {Name}, AuthType = {AuthType}, Secret = *** }}";
}

/// <summary>Runner reply. A failed operation is still HTTP 200 with <see cref="Success"/> = false.</summary>
public sealed record ConnectorRunResponse(bool Success, JsonElement? Output, string? Error, bool Retryable);

/// <summary>
/// HMAC-SHA256 request signing between API and runner: signature over "{unixSeconds}.{body}" with a
/// shared secret, sent as <c>X-Flowamaz-Runner-Timestamp</c> / <c>X-Flowamaz-Runner-Signature</c>.
/// Requests older than <see cref="MaxSkew"/> are rejected so a captured request cannot be replayed.
/// </summary>
public static class RunnerSignature
{
    public const string TimestampHeader = "X-Flowamaz-Runner-Timestamp";
    public const string SignatureHeader = "X-Flowamaz-Runner-Signature";
    public static readonly TimeSpan MaxSkew = TimeSpan.FromSeconds(60);

    public static string Compute(string secret, long unixSeconds, string body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{unixSeconds}.{body}")))
            .ToLowerInvariant();

    public static bool Verify(string secret, string? timestamp, string? signature, string body, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(secret) || !long.TryParse(timestamp, out var ts) || string.IsNullOrEmpty(signature)) return false;
        if ((now - DateTimeOffset.FromUnixTimeSeconds(ts)).Duration() > MaxSkew) return false;
        var expected = Encoding.ASCII.GetBytes(Compute(secret, ts, body));
        var actual = Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
