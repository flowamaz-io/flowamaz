namespace Flowamaz.Core.Enums;

/// <summary>
/// Lifecycle state of an <see cref="Entities.Platform.Organisation"/> (prompt 02 spec).
/// Stored as a string column so values stay readable in the database.
/// </summary>
public enum OrgStatus
{
    Trial,
    Active,
    Suspended,
    Cancelled,
}
