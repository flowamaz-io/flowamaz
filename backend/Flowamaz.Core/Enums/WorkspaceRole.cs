namespace Flowamaz.Core.Enums;

/// <summary>
/// Workspace-scoped role with an explicit numeric value so the hierarchy can be compared
/// directly (FUNCTIONAL.md §4.4): Admin=5, Designer=4, Operator=3, Runner=2, Viewer=1.
/// A request requiring a minimum role passes when <c>(int)actual &gt;= (int)minimum</c>.
/// Stored as a string column so values stay readable in the database.
/// </summary>
public enum WorkspaceRole
{
    Viewer = 1,
    Runner = 2,
    Operator = 3,
    Designer = 4,
    Admin = 5,
}
