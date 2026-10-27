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

    /// <summary>Created by an SSO group mapping; SSO removes it again when the person leaves the group.</summary>
    public bool ManagedBySso { get; set; }
}
