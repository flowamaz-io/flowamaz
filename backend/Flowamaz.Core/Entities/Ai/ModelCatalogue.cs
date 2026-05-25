namespace Flowamaz.Core.Entities.Ai;

/// <summary>
/// Catalogue of every model accepted by the platform with its capability flags.
/// Capability enforcement (FUNCTIONAL.md §5.4) reads from this table at config-save time:
/// F3 requires HasVision=true; F7 requires MaxContextTokens ≥ 100000.
/// </summary>
public class ModelCatalogue
{
    /// <summary>Internal catalogue key — dash-normalised, stable, used as the primary key and in config JSON.</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>The real model identifier sent to the provider API (e.g. "gemini-2.0-flash", not the dash-normalised "gemini-2-0-flash").</summary>
    public string ProviderModelId { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool HasVision { get; set; }
    public int MaxContextTokens { get; set; }
    public bool SupportsJsonMode { get; set; }
    public bool SupportsStreaming { get; set; }
    public bool IsEnabled { get; set; } = true;
    public List<string> PlanAccess { get; set; } = []; // jsonb string[]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
