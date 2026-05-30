using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Models;

/// <summary>
/// A workspace overview card for the multi-workspace overview page: identity plus the counts and
/// status the UI shows per workspace, and the current user's role in it.
/// </summary>
public sealed record WorkspaceOverviewItem(
    Guid Id,
    string Name,
    string Slug,
    string UserRole,
    int MemberCount,
    int WorkflowCount,
    DateTime? LastActiveAt,
    WorkspaceStatus Status);
