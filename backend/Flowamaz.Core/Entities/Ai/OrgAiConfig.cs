namespace Flowamaz.Core.Entities.Ai;

/// <summary>
/// Org-level overrides for AI config. Middle of resolution hierarchy (FUNCTIONAL.md §5.3):
/// workspace → org → platform. function_overrides maps function_id → ModelOverride JSON.
/// </summary>
public class OrgAiConfig
{
    public Guid OrgId { get; set; }
    /// <summary>jsonb: ["anthropic","azure-openai",...] — providers the org has enabled.</summary>
    public List<string> ProvidersEnabled { get; set; } = [];
    /// <summary>jsonb: { "copilot": { "provider":"...", "modelId":"...", "keySource":"Byok" }, ... }</summary>
    public string FunctionOverridesJson { get; set; } = "{}";
    /// <summary>jsonb: list of function IDs the org has locked — workspace cannot override.</summary>
    public List<string> WorkspaceLockedFunctions { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
