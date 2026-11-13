namespace Flowamaz.Core.Models;

// The Library as a workspace sees it (FUNCTIONAL.md §23), as served to the web app and the `fmz library` commands.

/// <summary>One release in a connector's changelog.</summary>
public sealed record ChangelogEntry(string Version, string Notes, DateOnly? ReleasedOn = null);

/// <summary>A Library connector as listed for a workspace.</summary>
public sealed record LibraryConnectorItem(
    string Id,
    string Publisher,
    string Name,
    string Version,
    string Category,
    string[] Tags,
    bool IsOfficial,
    // The pin a new install gets, e.g. "flowamaz-io/slack@2".
    string Pin,
    bool Installed,
    // What this workspace runs, when installed (it may be an older major).
    string? InstalledVersion,
    string? InstalledPin,
    DateTime? DeprecatedAt,
    DateTime? RemovalAt,
    string? DeprecationNote);

public sealed record LibraryConnectorDetail(
    LibraryConnectorItem Connector,
    IReadOnlyList<string> Operations,
    IReadOnlyList<ChangelogEntry> Changelog,
    int InstallCount,
    decimal AverageRating);

/// <summary>An installed connector that needs attention: a newer major to upgrade to, or a deprecation notice.</summary>
public sealed record LibraryOutdatedItem(
    string Id,
    string Publisher,
    string Name,
    string InstalledVersion,
    string InstalledPin,
    // Newest version of a higher major, or null when there is none (deprecation only).
    string? LatestVersion,
    // Release notes between the installed version and the latest, newest first.
    IReadOnlyList<ChangelogEntry> Changes,
    DateTime? DeprecatedAt,
    DateTime? RemovalAt,
    string? DeprecationNote);

public sealed record LibraryUpdateResult(string Id, string FromVersion, string ToVersion, string Pin);

/// <summary>A Library template as listed for a workspace.</summary>
public sealed record LibraryTemplateItem(
    Guid Id,
    string Slug,
    string Name,
    string Description,
    string Category,
    string Version,
    string? Publisher,
    bool IsOfficial,
    string[] Tags,
    int InstallCount,
    DateTime? DeprecatedAt,
    DateTime? RemovalAt);

public sealed record LibraryPublishResult(string Kind, string Name, string Status, string Message, string? PullRequestUrl = null);

/// <summary>What one sync run did, per repository.</summary>
public sealed record LibrarySyncReport(
    string Source,
    string Outcome,
    string? CommitSha,
    int Added,
    int Updated,
    int Deprecated,
    IReadOnlyList<string> Skipped,
    string? Error = null);

public sealed record LibrarySyncStatus(
    string Source,
    string Repository,
    string? LastCommitSha,
    DateTime? LastSuccessAt,
    DateTime? LastAttemptAt,
    int ConsecutiveFailures,
    string? LastError);
