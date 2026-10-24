using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Exceptions;

/// <summary>What went wrong when calling an AI provider — drives the user-facing message and status.</summary>
public enum AiProviderFailureKind
{
    /// <summary>401/403: the key is invalid, revoked or lacks permission. Retrying cannot help.</summary>
    AuthenticationFailed,

    /// <summary>429: the provider is throttling this key. Retrying after a short wait helps.</summary>
    RateLimited,

    /// <summary>5xx, timeout or network failure: provider outage. Usually temporary.</summary>
    Unavailable,

    /// <summary>Any other 4xx: the provider refused the request (e.g. an unknown model id).</summary>
    Rejected,
}

/// <summary>
/// Thrown by the AI completion layer when the provider call fails, translating raw HTTP failures
/// (<c>HttpRequestException</c> "Response status code does not indicate success: 401") into an
/// actionable message per CLAUDE.md rule 8. The raw provider error is logged by the caller, never
/// put in the message. Being an <see cref="AppException"/>, it flows through the global exception
/// middleware (and the creation endpoints' AI catch blocks) with its own code and status.
/// </summary>
public sealed class AiProviderException : AppException
{
    public AiProviderFailureKind Kind { get; }

    /// <summary>The provider's HTTP status, when there was one (null for timeouts/network errors).</summary>
    public int? ProviderStatusCode { get; }

    public AiProviderException(
        AiProviderFailureKind kind, string provider, AiKeySource keySource, int? providerStatusCode, Exception? inner = null)
        : base(CodeFor(kind), MessageFor(kind, provider, keySource, providerStatusCode), inner ?? new InvalidOperationException(kind.ToString()), StatusFor(kind))
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>Maps a provider HTTP status code to a failure kind. Only call for non-success codes.</summary>
    public static AiProviderFailureKind KindFor(int statusCode) => statusCode switch
    {
        401 or 403 => AiProviderFailureKind.AuthenticationFailed,
        429 => AiProviderFailureKind.RateLimited,
        408 or >= 500 => AiProviderFailureKind.Unavailable,
        _ => AiProviderFailureKind.Rejected,
    };

    private static string CodeFor(AiProviderFailureKind kind) => kind switch
    {
        AiProviderFailureKind.AuthenticationFailed => "AI_PROVIDER_AUTH_FAILED",
        AiProviderFailureKind.RateLimited => "AI_PROVIDER_RATE_LIMITED",
        AiProviderFailureKind.Unavailable => "AI_PROVIDER_UNAVAILABLE",
        _ => "AI_PROVIDER_REJECTED",
    };

    // 502 for a key/request the provider refused (our configuration, not the user's input), 429 for
    // provider throttling, 503 for an outage. None of these is the caller's network connection.
    private static int StatusFor(AiProviderFailureKind kind) => kind switch
    {
        AiProviderFailureKind.RateLimited => 429,
        AiProviderFailureKind.Unavailable => 503,
        _ => 502,
    };

    private static string ProviderName(string provider) =>
        string.IsNullOrWhiteSpace(provider) ? "The AI provider"
        : provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase) ? "Anthropic"
        : provider.Equals("google", StringComparison.OrdinalIgnoreCase) ? "Google"
        : provider;

    private static string MessageFor(AiProviderFailureKind kind, string provider, AiKeySource keySource, int? status)
    {
        var name = ProviderName(provider);
        var http = status is { } s ? $" (HTTP {s})" : string.Empty;
        return kind switch
        {
            AiProviderFailureKind.AuthenticationFailed when keySource == AiKeySource.Byok =>
                $"AI request failed: {name} rejected this workspace's own (BYOK) AI key{http}. The key is invalid, revoked or lacks access to the model. " +
                "A workspace admin must update or remove it in Settings → Workspace → AI model configuration. Retrying won't help until the key is fixed.",
            AiProviderFailureKind.AuthenticationFailed =>
                $"AI request failed: {name} rejected the platform AI key{http}. The key is invalid or has been revoked. " +
                "A platform administrator must set a valid key in Ai__AnthropicPlatformKey and restart the API, or a workspace admin can add " +
                "a BYOK key in Settings → Workspace → AI model configuration. Retrying won't help until the key is fixed.",
            AiProviderFailureKind.RateLimited =>
                $"AI request failed: {name} is rate-limiting requests for this AI key{http}, so nothing was generated. " +
                "Wait about a minute and try again. If it keeps happening, spread requests out or raise the key's rate limit with the provider.",
            AiProviderFailureKind.Unavailable =>
                $"AI request failed: {name} is unavailable or did not respond in time{http}, so nothing was generated. " +
                "This is usually temporary. Wait a few minutes and try again; if it persists, check the provider's status page.",
            _ =>
                $"AI request failed: {name} refused the request{http}. The configured model may not exist or not be enabled for this key. " +
                "Check the model chosen in Settings → Workspace → AI model configuration, then try again.",
        };
    }
}
