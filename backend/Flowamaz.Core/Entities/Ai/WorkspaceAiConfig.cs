namespace Flowamaz.Core.Entities.Ai;

/// <summary>
/// Workspace-level overrides for AI config. Top of resolution hierarchy (workflow YAML and
/// node-level config beat this for F4 only). function_overrides maps function_id → ModelOverride JSON.
/// </summary>
public class WorkspaceAiConfig
{
    public Guid WorkspaceId { get; set; }
    /// <summary>jsonb: { "copilot": { "provider":"...", "modelId":"...", "keySource":"Byok" }, ... }</summary>
    public string FunctionOverridesJson { get; set; } = "{}";
    /// <summary>jsonb: { "anthropic": "cred_abc", "azure-openai": "cred_def" } — BYOK credential alias per provider.</summary>
    public string ByokCredentialRefsJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
