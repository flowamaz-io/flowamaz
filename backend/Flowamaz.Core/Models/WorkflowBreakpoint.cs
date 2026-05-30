namespace Flowamaz.Core.Models;

/// <summary>
/// A development-only breakpoint (prompt 07-04): pause an instance before executing
/// <see cref="NodeId"/> of <see cref="WorkflowId"/>. Held purely in memory — never persisted —
/// and only honoured in the Development environment.
/// </summary>
public sealed record WorkflowBreakpoint(Guid Id, Guid WorkspaceId, Guid WorkflowId, string NodeId);
