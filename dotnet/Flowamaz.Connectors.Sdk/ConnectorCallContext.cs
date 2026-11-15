namespace Flowamaz.Application.Connectors;

/// <summary>
/// Scoped per request/job: identifies who a connector call runs for. Set by the engine
/// (<see cref="Services.ConnectorActionExecutor"/>) before invoking a handler, so handlers that must
/// isolate by workspace (file system, script) never take the workspace id from user-controlled input.
/// </summary>
public sealed class ConnectorCallContext
{
    public Guid? WorkspaceId { get; set; }
    public Guid? InstanceId { get; set; }
    public string? NodeId { get; set; }

    public Guid RequireWorkspace(string op) => WorkspaceId
        ?? throw new InvalidOperationException($"{op}: no workspace context — this operation can only run inside a workflow.");
}
