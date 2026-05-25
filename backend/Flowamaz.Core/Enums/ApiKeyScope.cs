namespace Flowamaz.Core.Enums;

/// <summary>
/// Canonical API key scope strings (FUNCTIONAL.md §4.6). Stored as a string list on
/// <see cref="Entities.Workspaces.WorkspaceApiKey"/> and enforced on every API call.
/// Modelled as constants (not an enum) because scopes are persisted and transported as strings.
/// </summary>
public static class ApiKeyScope
{
    public const string WorkflowsRead = "workflows:read";
    public const string WorkflowsTrigger = "workflows:trigger";
    public const string InstancesRead = "instances:read";
    public const string InstancesWrite = "instances:write";
    public const string WebhooksManage = "webhooks:manage";
    public const string InstancesVariablesRead = "instances:variables:read";

    /// <summary>Every recognised scope — used to validate scope lists at key-creation time.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        WorkflowsRead,
        WorkflowsTrigger,
        InstancesRead,
        InstancesWrite,
        WebhooksManage,
        InstancesVariablesRead,
    ];

    public static bool IsValid(string scope) => All.Contains(scope);
}
