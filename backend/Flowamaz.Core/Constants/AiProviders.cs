namespace Flowamaz.Core.Constants;

/// <summary>
/// Canonical string identifiers for AI providers (FUNCTIONAL.md §5.1).
/// </summary>
public static class AiProviders
{
    public const string Anthropic = "anthropic";
    public const string AzureOpenAi = "azure-openai";
    public const string Google = "google";
    public const string Kimi = "kimi";
    public const string Mistral = "mistral";
    public const string Byom = "byom";

    /// <summary>Every recognised provider id — used to validate provider lists.</summary>
    public static readonly IReadOnlyList<string> All = [Anthropic, AzureOpenAi, Google, Kimi, Mistral, Byom];
}
