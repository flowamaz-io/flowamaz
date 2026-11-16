namespace Flowamaz.Core.Enums;

/// <summary>
/// The three environments provisioned for every workspace (FUNCTIONAL.md §2.1).
/// Production keys are minted as <c>fmz_live_*</c>; Dev/Staging as <c>fmz_test_*</c>.
/// Stored as a string column so values stay readable in the database.
/// </summary>
public enum WorkspaceEnvironmentType
{
    Dev,
    Staging,
    Production,
}
