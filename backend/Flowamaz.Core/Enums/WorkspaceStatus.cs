namespace Flowamaz.Core.Enums;

/// <summary>
/// Lifecycle status of a workspace. Archived workspaces preserve all data but reject new triggers;
/// they can be restored to Active. Stored as a string column.
/// </summary>
public enum WorkspaceStatus
{
    Active,
    Archived,
}
