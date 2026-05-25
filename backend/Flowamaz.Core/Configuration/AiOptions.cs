namespace Flowamaz.Core.Configuration;

/// <summary>Binds the "Ai" section of appsettings.json — never read AI config strings inline.</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public string AnthropicPlatformKey { get; set; } = string.Empty;
    public long BudgetDefaultMonthlyTokens { get; set; } = 500_000;
    public int CopilotRateLimitPerHour { get; set; } = 60;
    public int SemanticCacheTtlHours { get; set; } = 24;
}
