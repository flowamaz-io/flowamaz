using Flowamaz.Core.Enums;
using Flowamaz.Core.Models;

namespace Flowamaz.Core.Entities.Workspaces;

/// <summary>
/// A team/department within an organisation — tier 3 of the four-tier hierarchy
/// (FUNCTIONAL.md §2.1) and the primary security boundary in Flowamaz. <see cref="Slug"/>
/// is unique per org. Soft-deletable. References its <see cref="OrgId"/> by scalar key,
/// matching the scaffold's convention of scalar keys over EF navigations.
/// </summary>
public class Workspace : BaseEntity
{
    public Guid OrgId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public WorkspaceSettings Settings { get; set; } = new();

    /// <summary>Lifecycle status. Archived workspaces reject new triggers but keep all data.</summary>
    public WorkspaceStatus Status { get; set; } = WorkspaceStatus.Active;
}
