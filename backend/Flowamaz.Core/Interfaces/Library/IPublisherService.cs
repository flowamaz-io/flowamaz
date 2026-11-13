using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Library;

/// <summary>Library publishers: profiles, the portal and staff reviews (FUNCTIONAL.md §23.9).</summary>
public interface IPublisherService
{
    /// <summary>A publisher's public profile: only items that are live in the Library.</summary>
    Task<PublisherProfileView> GetProfileAsync(string publisherId, CancellationToken ct = default);

    /// <summary>The signed-in organisation's portal; refreshes open pull requests from GitHub (best effort).</summary>
    Task<PublisherPortal> GetPortalAsync(Guid orgId, CancellationToken ct = default);

    Task<PublisherProfileView> UpdateProfileAsync(Guid orgId, UpdatePublisherProfileRequest request, Guid userId, CancellationToken ct = default);

    Task<LibraryReviewQueue> GetReviewQueueAsync(CancellationToken ct = default);
    Task<LibraryReviewTemplate> ApproveTemplateAsync(Guid templateId, string? notes, Guid staffUserId, CancellationToken ct = default);
    Task<LibraryReviewTemplate> RejectTemplateAsync(Guid templateId, string notes, Guid staffUserId, CancellationToken ct = default);
}
