namespace Flowamaz.Core.Models;

// Library publishers (FUNCTIONAL.md §23.9): public contributor profiles, the publisher portal, staff reviews.

/// <summary>One Library item on a publisher's profile or in their portal.</summary>
public sealed record PublisherItem(
    string Kind,
    string Id,
    string Name,
    string Version,
    string Category,
    int Installs,
    decimal AverageRating,
    int RatingCount,
    DateTime? DeprecatedAt,
    string? DeprecationNote,
    DateTime UpdatedAt);

public sealed record PublisherTotals(int Items, int Installs, decimal AverageRating, int RatingCount);

public sealed record PublisherProfileView(
    string PublisherId,
    string DisplayName,
    string Bio,
    string? WebsiteUrl,
    string? GithubLogin,
    bool IsVerified,
    bool IsOfficial,
    DateTime? PublishingSince,
    PublisherTotals Totals,
    IReadOnlyList<PublisherItem> Items);

/// <summary>
/// Something this organisation submitted to the Library and where it stands. <see cref="Status"/> is one of
/// pending, merged, live, approved (templates) or rejected; <see cref="StatusText"/> says what happens next.
/// </summary>
public sealed record PublisherSubmission(
    Guid Id,
    string Kind,
    string Name,
    string? LibraryId,
    string Status,
    string StatusText,
    string? PullRequestUrl,
    string? ReviewerNotes,
    DateTime SubmittedAt,
    DateTime? DecidedAt,
    DateTime? CheckedAt);

public sealed record PublisherPortal(
    string PublisherId,
    bool HasProfile,
    PublisherProfileView Profile,
    IReadOnlyList<PublisherSubmission> Submissions);

public sealed record UpdatePublisherProfileRequest(string DisplayName, string? Bio, string? WebsiteUrl, string? GithubLogin);

/// <summary>A template waiting for a staff decision.</summary>
public sealed record LibraryReviewTemplate(
    Guid Id,
    string Name,
    string Slug,
    string Version,
    string Category,
    string Description,
    string PublisherId,
    string OrgName,
    string YamlContent,
    DateTime SubmittedAt);

public sealed record LibraryReviewQueue(
    IReadOnlyList<LibraryReviewTemplate> Templates,
    IReadOnlyList<PublisherSubmission> ConnectorPullRequests);
