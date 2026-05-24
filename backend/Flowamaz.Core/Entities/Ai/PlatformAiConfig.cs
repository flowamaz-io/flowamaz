using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Entities.Ai;

/// <summary>
/// Platform-wide default model assignment for each of the 7 AI functions.
/// Bottom of the resolution hierarchy (FUNCTIONAL.md §5.3): workspace → org → platform.
/// Natural key: FunctionId. Seeded in migration with values from FUNCTIONAL.md §5.2.
/// </summary>
public class PlatformAiConfig
{
    public string FunctionId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public AiKeySource KeySource { get; set; }
    public bool IsEnabled { get; set; } = true;
    public List<string> PlanAccess { get; set; } = []; // jsonb string[] — e.g. ["community","starter","pro","enterprise"]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
