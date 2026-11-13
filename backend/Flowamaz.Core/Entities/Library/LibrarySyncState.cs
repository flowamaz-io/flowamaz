namespace Flowamaz.Core.Entities.Library;

/// <summary>
/// Where the Library GitHub sync (FUNCTIONAL.md §23.7) stands for one repository: the last commit applied and
/// the run of failures since the last success. A failed sync changes nothing, so the Library keeps serving the
/// last known state; the platform team is alerted once after three failures in a row.
/// </summary>
public class LibrarySyncState : BaseEntity
{
    /// <summary>"connectors" or "templates".</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>owner/repo the source reads.</summary>
    public string Repository { get; set; } = string.Empty;

    public string? LastCommitSha { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? LastError { get; set; }

    /// <summary>Set when the platform team was alerted about this run of failures; cleared on the next success.</summary>
    public DateTime? AlertedAt { get; set; }
}
