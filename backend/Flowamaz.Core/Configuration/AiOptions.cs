namespace Flowamaz.Core.Configuration;

/// <summary>Binds the "Ai" section of appsettings.json — never read AI config strings inline.</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public string AnthropicPlatformKey { get; set; } = string.Empty;

    /// <summary>
    /// Forces <c>AiCompletionService</c> to return the deterministic local stub instead of calling
    /// the provider, even when a key is configured. For local/E2E runs that need F5 model resolution
    /// to succeed (a key must be present) but must not make a billed call. Off in production.
    /// </summary>
    public bool UseStubCompletion { get; set; }
    public long BudgetDefaultMonthlyTokens { get; set; } = 500_000;
    public int CopilotRateLimitPerHour { get; set; } = 60;
    public int SemanticCacheTtlHours { get; set; } = 24;
}
