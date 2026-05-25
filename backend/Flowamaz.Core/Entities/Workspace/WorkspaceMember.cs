using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Entities.Workspaces;

/// <summary>
/// Binds an <see cref="Platform.OrgUser"/> to a <see cref="Workspace"/> with exactly one
/// <see cref="WorkspaceRole"/> (unique on WorkspaceId + OrgUserId). <see cref="IsActive"/>
/// gates participation without deleting history.
/// </summary>
public class WorkspaceMember : BaseEntity
{
    public Guid WorkspaceId { get; set; }
    public Guid OrgUserId { get; set; }
    public WorkspaceRole Role { get; set; } = WorkspaceRole.Viewer;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
