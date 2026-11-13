using System.Text.RegularExpressions;
using Flowamaz.Application.Library;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Entities.Library;
using Flowamaz.Core.Entities.Notifications;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Library;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services.Library;

/// <summary>
/// Library publishers (FUNCTIONAL.md §23.9): public contributor profiles, the publisher portal (profile, items,
/// submissions) and the staff review queue for templates published from the app.
/// </summary>
public sealed partial class PublisherService : IPublisherService
{
    public const string OfficialPublisher = LibraryManifestParser.OfficialPublisher;

    /// <summary>Pull requests are re-read from GitHub at most this often, and at most this many per portal load.</summary>
    internal static readonly TimeSpan PrRecheckAfter = TimeSpan.FromMinutes(10);
    internal const int MaxPrChecksPerLoad = 10;

    private readonly FlowAmazDbContext _db;
    private readonly IConnectorSubmissionPrService _pullRequests;
    private readonly ILogger<PublisherService> _log;
    private readonly TimeProvider _clock;

    public PublisherService(FlowAmazDbContext db, IConnectorSubmissionPrService pullRequests, ILogger<PublisherService> log,
        TimeProvider? clock = null)
    {
        _db = db;
        _pullRequests = pullRequests;
        _log = log;
        _clock = clock ?? TimeProvider.System;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    // ── Public profile ──────────────────────────────────────────────────────────────

    public async Task<PublisherProfileView> GetProfileAsync(string publisherId, CancellationToken ct = default)
    {
        _log.LogInformation("PublisherService.GetProfileAsync entry publisher={Publisher}", publisherId);
        var id = (publisherId ?? "").Trim().ToLowerInvariant();
        if (!PublisherIdRegex().IsMatch(id)) throw LibraryException.PublisherNotFound(publisherId ?? "");

        var profile = await _db.PublisherProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.PublisherId == id, ct);
        var ownerOrg = await OwnerOrgAsync(id, profile, ct);
        var items = await LiveItemsAsync(id, ownerOrg?.Id, ct);
        if (profile is null && ownerOrg is null && items.Count == 0) throw LibraryException.PublisherNotFound(id);

        var view = ToView(id, profile, ownerOrg?.Name, items);
        _log.LogInformation("PublisherService.GetProfileAsync exit publisher={Publisher} items={Items}", id, items.Count);
        return view;
    }

    private async Task<Flowamaz.Core.Entities.Platform.Organisation?> OwnerOrgAsync(string publisherId, PublisherProfile? profile, CancellationToken ct)
    {
        if (profile?.OrgId is { } orgId) return await _db.Organisations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId, ct);
        if (publisherId == OfficialPublisher) return null;
        // An organisation publishes under its slug (or org-{8 hex} when it has none).
        var bySlug = await _db.Organisations.AsNoTracking().FirstOrDefaultAsync(o => o.Slug == publisherId, ct);
        if (bySlug is not null) return bySlug;
        var m = FallbackIdRegex().Match(publisherId);
        if (!m.Success) return null;
        var prefix = m.Groups[1].Value;
        var candidates = await _db.Organisations.AsNoTracking().Where(o => o.Slug == null || o.Slug == "").ToListAsync(ct);
        return candidates.FirstOrDefault(o => o.Id.ToString("N").StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>Only what anyone can install: never a workspace's own forms, pending or rejected templates.</summary>
    private async Task<List<PublisherItem>> LiveItemsAsync(string publisherId, Guid? ownerOrgId, CancellationToken ct)
    {
        var cutoff = Now.AddDays(-ConnectorDefinition.DeprecationNoticeDays);
        var official = publisherId == OfficialPublisher;
        var items = new List<PublisherItem>();

        var connectors = await _db.ConnectorDefinitions.AsNoTracking()
            .Where(c => c.WorkspaceId == null && c.IsEnabled && c.IsLatestMajor && c.PublisherId == publisherId)
            .ToListAsync(ct);
        items.AddRange(connectors
            .Where(c => c.DeprecatedAt is null || c.DeprecatedAt > cutoff)
            .Select(c => new PublisherItem("connector", c.ConnectorId, c.DisplayName, c.Version, c.Category, c.InstallCount,
                c.AverageRating, c.RatingCount, c.DeprecatedAt, c.DeprecationNote, c.UpdatedAt)));

        var templates = await _db.WorkflowTemplates.AsNoTracking()
            .Where(t => t.IsActive && t.ReviewStatus == "approved"
                        && (t.PublisherId == publisherId
                            || (official && t.IsOfficial && t.PublisherId == null)
                            || (ownerOrgId != null && t.PublisherId == null && !t.IsOfficial && t.OrgId == ownerOrgId)))
            .ToListAsync(ct);
        items.AddRange(templates
            .Where(t => t.DeprecatedAt is null || t.DeprecatedAt > cutoff)
            .Select(t => new PublisherItem("template", t.Slug, t.Name, t.Version, t.Category, t.InstallCount,
                t.AverageRating, 0, t.DeprecatedAt, t.DeprecationNote, t.UpdatedAt)));

        var forms = await _db.FormDefinitions.AsNoTracking()
            .Where(f => f.WorkspaceId == null && f.PublisherId == publisherId).ToListAsync(ct);
        items.AddRange(forms
            .Where(f => f.DeprecatedAt is null || f.DeprecatedAt > cutoff)
            .Select(f => new PublisherItem("form", f.Slug, f.Name, f.Version, f.Category, 0, 0, 0, f.DeprecatedAt, f.DeprecationNote, f.UpdatedAt)));

        var packs = await _db.PackDefinitions.AsNoTracking().Where(p => p.PublisherId == publisherId).ToListAsync(ct);
        items.AddRange(packs
            .Where(p => p.DeprecatedAt is null || p.DeprecatedAt > cutoff)
            .Select(p => new PublisherItem("pack", p.Slug, p.Name, p.Version, p.Category, 0, 0, 0, p.DeprecatedAt, p.DeprecationNote, p.UpdatedAt)));

        return items
            .OrderBy(i => KindOrder(i.Kind)).ThenByDescending(i => i.Installs).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int KindOrder(string kind) => kind switch { "connector" => 0, "template" => 1, "form" => 2, _ => 3 };

    private static PublisherProfileView ToView(string publisherId, PublisherProfile? profile, string? orgName, IReadOnlyList<PublisherItem> items)
    {
        var rated = items.Where(i => i.RatingCount > 0).ToList();
        var ratingCount = rated.Sum(i => i.RatingCount);
        var average = ratingCount == 0 ? 0 : Math.Round(rated.Sum(i => i.AverageRating * i.RatingCount) / ratingCount, 1);
        var official = publisherId == OfficialPublisher;
        return new PublisherProfileView(
            publisherId,
            profile?.DisplayName is { Length: > 0 } name ? name : official ? "Flowamaz" : orgName ?? publisherId,
            profile?.Bio ?? "",
            profile?.WebsiteUrl,
            profile?.GithubLogin,
            official || profile?.IsVerified == true,
            official,
            items.Count == 0 ? null : items.Min(i => i.UpdatedAt),
            new PublisherTotals(items.Count, items.Sum(i => i.Installs), average, ratingCount),
            items);
    }

    // ── Portal ──────────────────────────────────────────────────────────────────────

    public async Task<PublisherPortal> GetPortalAsync(Guid orgId, CancellationToken ct = default)
    {
        _log.LogInformation("PublisherService.GetPortalAsync entry org={OrgId}", orgId);
        var org = await _db.Organisations.AsNoTracking().FirstAsync(o => o.Id == orgId, ct);
        var publisherId = PublisherIds.ForOrg(org.Slug, orgId);
        var profile = await _db.PublisherProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.PublisherId == publisherId, ct);
        var items = await LiveItemsAsync(publisherId, orgId, ct);

        await RefreshConnectorSubmissionsAsync(orgId, ct);

        var submissions = new List<PublisherSubmission>();
        var connectorSubs = await _db.ConnectorSubmissions.AsNoTracking().Where(s => s.OrgId == orgId)
            .OrderByDescending(s => s.CreatedAt).Take(100).ToListAsync(ct);
        submissions.AddRange(connectorSubs.Select(ToSubmission));
        var templateSubs = await _db.WorkflowTemplates.AsNoTracking()
            .Where(t => t.OrgId == orgId && !t.IsOfficial && t.PublisherId == null)
            .OrderByDescending(t => t.CreatedAt).Take(100).ToListAsync(ct);
        submissions.AddRange(templateSubs.Select(ToSubmission));

        var portal = new PublisherPortal(publisherId, profile is not null, ToView(publisherId, profile, org.Name, items),
            submissions.OrderByDescending(s => s.SubmittedAt).ToList());
        _log.LogInformation("PublisherService.GetPortalAsync exit org={OrgId} items={Items} submissions={Submissions}",
            orgId, items.Count, submissions.Count);
        return portal;
    }

    /// <summary>
    /// Brings connector submissions up to date: live once the sync has the connector; otherwise the pull request's
    /// state from GitHub (merged → waiting for the sync, closed → rejected). GitHub being unreachable keeps the
    /// last known status.
    /// </summary>
    internal async Task RefreshConnectorSubmissionsAsync(Guid orgId, CancellationToken ct)
    {
        var open = await _db.ConnectorSubmissions
            .Where(s => s.OrgId == orgId && (s.Status == "pending" || s.Status == "merged" || s.Status == "approved"))
            .ToListAsync(ct);
        if (open.Count == 0) return;

        var keys = open.Where(s => s.PublisherId != null && s.ConnectorId != null).Select(s => s.PublisherId + "/" + s.ConnectorId).ToHashSet();
        var live = (await _db.ConnectorDefinitions.AsNoTracking().Where(c => c.WorkspaceId == null)
                .Select(c => new { c.PublisherId, c.ConnectorId }).ToListAsync(ct))
            .Select(c => c.PublisherId + "/" + c.ConnectorId).Where(keys.Contains).ToHashSet();

        var checks = 0;
        foreach (var s in open.OrderBy(s => s.PrCheckedAt ?? DateTime.MinValue))
        {
            if (s.PublisherId != null && s.ConnectorId != null && live.Contains(s.PublisherId + "/" + s.ConnectorId))
            {
                s.Status = "live";
                s.DecidedAt ??= Now;
                continue;
            }
            if (s.Status != "pending" || string.IsNullOrEmpty(s.GithubPrUrl)) continue;
            if (s.PrCheckedAt is { } last && Now - last < PrRecheckAfter) continue;
            if (checks++ >= MaxPrChecksPerLoad) continue;

            var state = await _pullRequests.GetPullRequestStateAsync(s.GithubPrUrl, ct);
            if (state is null) continue;
            s.PrCheckedAt = Now;
            if (state == "merged")
            {
                s.Status = "merged";
                s.DecidedAt = Now;
            }
            else if (state == "closed")
            {
                s.Status = "rejected";
                s.DecidedAt = Now;
                s.ReviewerNotes ??= "The pull request was closed without merging. Read the review comments on GitHub, fix the connector, then run fmz connector publish again.";
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    private static PublisherSubmission ToSubmission(ConnectorSubmission s)
    {
        var status = s.Status == "approved" ? "merged" : s.Status;
        var text = status switch
        {
            "live" => "Live in the Library.",
            "merged" => "Merged. It goes live with the next Library sync, within 15 minutes.",
            "rejected" => "Not accepted. See the reviewer's note, fix the connector and submit it again.",
            _ => "Pull request open. Automated checks run first; it merges when they pass, or after a review for "
                 + "wildcard domains, custom auth, cloud-provider APIs or new publishers.",
        };
        var libraryId = s.PublisherId != null && s.ConnectorId != null ? s.PublisherId + "/" + s.ConnectorId : null;
        return new PublisherSubmission(s.Id, "connector", s.ConnectorName, libraryId, status, text, s.GithubPrUrl,
            s.ReviewerNotes, s.CreatedAt, s.DecidedAt, s.PrCheckedAt);
    }

    private static PublisherSubmission ToSubmission(WorkflowTemplate t)
    {
        var text = t.ReviewStatus switch
        {
            "approved" => t.IsActive ? "Live in the Library." : "Approved, but switched off. Contact support to turn it back on.",
            "rejected" => "Not accepted. See the reviewer's note, fix the template and publish it again (fmz template publish).",
            _ => "Waiting for review. A Flowamaz reviewer checks every template published from the app before it appears in the Library.",
        };
        return new PublisherSubmission(t.Id, "template", t.Name, t.Slug, t.ReviewStatus, text, null, t.ReviewNotes,
            t.CreatedAt, t.ReviewedAt, null);
    }

    public async Task<PublisherProfileView> UpdateProfileAsync(Guid orgId, UpdatePublisherProfileRequest request, Guid userId, CancellationToken ct = default)
    {
        _log.LogInformation("PublisherService.UpdateProfileAsync entry org={OrgId}", orgId);
        var org = await _db.Organisations.AsNoTracking().FirstAsync(o => o.Id == orgId, ct);
        var publisherId = PublisherIds.ForOrg(org.Slug, orgId);
        if (publisherId == OfficialPublisher)
            throw LibraryException.ProfileInvalid("The official Flowamaz profile is maintained by the platform team, not from an organisation.");

        var (name, bio, website, github) = CheckProfile(request);
        var profile = await _db.PublisherProfiles.FirstOrDefaultAsync(p => p.PublisherId == publisherId, ct);
        if (profile is not null && profile.OrgId is { } owner && owner != orgId)
            throw LibraryException.ProfileInvalid($"The publisher id '{publisherId}' belongs to another organisation. Contact support if this is yours.");
        if (profile is null)
        {
            profile = new PublisherProfile { PublisherId = publisherId, OrgId = orgId, CreatedBy = userId };
            _db.PublisherProfiles.Add(profile);
        }
        profile.OrgId = orgId;
        profile.DisplayName = name;
        profile.Bio = bio;
        profile.WebsiteUrl = website;
        profile.GithubLogin = github;
        profile.UpdatedBy = userId;
        await _db.SaveChangesAsync(ct);

        var view = ToView(publisherId, profile, org.Name, await LiveItemsAsync(publisherId, orgId, ct));
        _log.LogInformation("PublisherService.UpdateProfileAsync exit publisher={Publisher}", publisherId);
        return view;
    }

    /// <summary>Validated, trimmed profile fields; throws a fix-it message for the first problem.</summary>
    internal static (string Name, string Bio, string? Website, string? Github) CheckProfile(UpdatePublisherProfileRequest r)
    {
        var name = (r.DisplayName ?? "").Trim();
        if (name.Length is 0 or > 100)
            throw LibraryException.ProfileInvalid("Give the publisher a display name of up to 100 characters, e.g. your company name.");
        var bio = (r.Bio ?? "").Trim();
        if (bio.Length > 600)
            throw LibraryException.ProfileInvalid($"The description is {bio.Length} characters; shorten it to 600 or fewer.");
        string? website = string.IsNullOrWhiteSpace(r.WebsiteUrl) ? null : r.WebsiteUrl.Trim();
        if (website is not null && (website.Length > 200 || !Uri.TryCreate(website, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw LibraryException.ProfileInvalid("The website must be a full https:// address of up to 200 characters, e.g. https://acme.example.");
        string? github = string.IsNullOrWhiteSpace(r.GithubLogin) ? null : r.GithubLogin.Trim().TrimStart('@');
        if (github is not null && github.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            github = github["https://github.com/".Length..].Trim('/');
        if (github is not null && !GithubLoginRegex().IsMatch(github))
            throw LibraryException.ProfileInvalid("The GitHub account must be a user or organisation name like acme-labs (letters, numbers and single hyphens, up to 39).");
        return (name, bio, website, github);
    }

    // ── Staff review ────────────────────────────────────────────────────────────────

    public async Task<LibraryReviewQueue> GetReviewQueueAsync(CancellationToken ct = default)
    {
        _log.LogInformation("PublisherService.GetReviewQueueAsync entry");
        var pending = await _db.WorkflowTemplates.AsNoTracking()
            .Where(t => t.ReviewStatus == "pending" && t.OrgId != null && !t.IsOfficial)
            .OrderBy(t => t.CreatedAt).Take(200).ToListAsync(ct);
        var orgIds = pending.Select(t => t.OrgId!.Value).Distinct().ToList();
        var orgs = await _db.Organisations.AsNoTracking().Where(o => orgIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var templates = pending.Select(t => ToReview(t, orgs.GetValueOrDefault(t.OrgId!.Value))).ToList();

        var prs = await _db.ConnectorSubmissions.AsNoTracking()
            .Where(s => s.Status == "pending" || s.Status == "merged" || s.Status == "approved")
            .OrderBy(s => s.CreatedAt).Take(200).ToListAsync(ct);

        _log.LogInformation("PublisherService.GetReviewQueueAsync exit templates={Templates} connectorPrs={Prs}", templates.Count, prs.Count);
        return new LibraryReviewQueue(templates, prs.Select(ToSubmission).ToList());
    }

    private static LibraryReviewTemplate ToReview(WorkflowTemplate t, Flowamaz.Core.Entities.Platform.Organisation? org) => new(
        t.Id, t.Name, t.Slug, t.Version, t.Category, t.Description,
        PublisherIds.ForOrg(org?.Slug, t.OrgId ?? Guid.Empty), org?.Name ?? "(deleted organisation)", t.YamlContent, t.CreatedAt);

    public Task<LibraryReviewTemplate> ApproveTemplateAsync(Guid templateId, string? notes, Guid staffUserId, CancellationToken ct = default) =>
        DecideAsync(templateId, approve: true, notes, staffUserId, ct);

    public Task<LibraryReviewTemplate> RejectTemplateAsync(Guid templateId, string notes, Guid staffUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(notes) || notes.Trim().Length < 10) throw LibraryException.ReviewNotesRequired();
        return DecideAsync(templateId, approve: false, notes, staffUserId, ct);
    }

    private async Task<LibraryReviewTemplate> DecideAsync(Guid templateId, bool approve, string? notes, Guid staffUserId, CancellationToken ct)
    {
        _log.LogInformation("PublisherService.DecideAsync entry template={TemplateId} approve={Approve}", templateId, approve);
        var template = await _db.WorkflowTemplates.FirstOrDefaultAsync(t => t.Id == templateId && t.ReviewStatus == "pending" && t.OrgId != null, ct)
                       ?? throw LibraryException.ReviewNotFound(templateId);
        var trimmed = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (trimmed is { Length: > 1000 }) trimmed = trimmed[..1000];

        // Ownership stays with the organisation (OrgId). PublisherId is for repository-synced templates only:
        // setting it here would let the sync deprecate this template as "removed from the repository".
        template.ReviewStatus = approve ? "approved" : "rejected";
        template.ReviewNotes = trimmed;
        template.ReviewedAt = Now;
        template.UpdatedBy = staffUserId;

        var owners = await _db.OrgUsers.Where(u => u.OrgId == template.OrgId && u.IsOrgOwner && u.IsActive).Select(u => u.Id).ToListAsync(ct);
        foreach (var owner in owners)
            _db.Notifications.Add(new Notification
            {
                UserId = owner,
                OrgId = template.OrgId!.Value,
                Type = approve ? "library_template_approved" : "library_template_rejected",
                Title = approve ? $"{Trim(template.Name, 150)} is live in the Library" : $"{Trim(template.Name, 150)} wasn't accepted",
                Message = approve
                    ? "Anyone can now install it from Library → Templates." + (trimmed is null ? "" : " Reviewer's note: " + trimmed)
                    : $"Reviewer's note: {trimmed} Fix the template and publish it again.",
                ActionUrl = "/settings/publisher",
            });
        await _db.SaveChangesAsync(ct);

        var org = await _db.Organisations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == template.OrgId, ct);
        _log.LogInformation("PublisherService.DecideAsync exit template={TemplateId} status={Status} notified={Owners}",
            templateId, template.ReviewStatus, owners.Count);
        return ToReview(template, org);
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex PublisherIdRegex();

    [GeneratedRegex("^org-([0-9a-f]{8})$")]
    private static partial Regex FallbackIdRegex();

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9])){0,38}$")]
    private static partial Regex GithubLoginRegex();
}
