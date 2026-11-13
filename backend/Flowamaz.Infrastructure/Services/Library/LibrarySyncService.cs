using System.Text.Json;
using Flowamaz.Application.Library;
using Flowamaz.Application.Platform;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Entities.Library;
using Flowamaz.Core.Entities.Notifications;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Library;
using Flowamaz.Core.Models;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Flowamaz.Infrastructure.Services.Library;

/// <summary>
/// Library GitHub sync (FUNCTIONAL.md §23.7) and connector versioning (§23.8).
/// <list type="bullet">
/// <item>Each run reads the connectors and templates repositories at their head commit and applies them in one
/// transaction per repository. A repository that can't be read leaves the Library as it was (last known state);
/// after three failures in a row the platform team is notified once.</item>
/// <item>A connector has one row per major version. A newer minor/patch updates that row in place: workspaces on
/// it run the new version straight away and their admins get a changelog notice. A new major is a new row:
/// workspaces stay pinned to their major until an admin upgrades.</item>
/// <item>A connector or template that disappears from its repository, or whose manifest says <c>deprecated</c>,
/// starts a 90-day notice: it keeps running where installed and leaves the install list when the notice ends.</item>
/// </list>
/// Only rows the sync owns are touched: platform-wide community connectors and Library templates. Official
/// connectors (code in this repository) and workspace-private connectors are left alone.
/// </summary>
public sealed class LibrarySyncService : ILibrarySyncService
{
    internal const int AlertAfterFailures = 3;
    internal const string RemovedNote = "Removed from the Library repository.";
    private static readonly string[] Sources = ["connectors", "templates"];

    private readonly FlowAmazDbContext _db;
    private readonly ILibrarySource _source;
    private readonly IConfiguration _config;
    private readonly ILogger<LibrarySyncService> _log;
    private readonly ISchedulerFactory? _scheduler;
    private readonly TimeProvider _clock;

    public LibrarySyncService(
        FlowAmazDbContext db,
        ILibrarySource source,
        IConfiguration config,
        ILogger<LibrarySyncService> log,
        ISchedulerFactory? scheduler = null,
        TimeProvider? clock = null)
    {
        _db = db;
        _source = source;
        _config = config;
        _log = log;
        _scheduler = scheduler;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<LibrarySyncStatus>> GetStatusAsync(CancellationToken ct = default) =>
        await _db.LibrarySyncStates.AsNoTracking().OrderBy(s => s.Source)
            .Select(s => new LibrarySyncStatus(s.Source, s.Repository, s.LastCommitSha, s.LastSuccessAt, s.LastAttemptAt, s.ConsecutiveFailures, s.LastError))
            .ToListAsync(ct);

    public async Task RequestSyncAsync(CancellationToken ct = default)
    {
        _log.LogInformation("LibrarySyncService.RequestSyncAsync entry scheduled={Scheduled}", _scheduler is not null);
        if (_scheduler is null) return; // background jobs off (tests): the 15-minute poll isn't running either
        var scheduler = await _scheduler.GetScheduler(ct);
        await scheduler.TriggerJob(new JobKey(nameof(Jobs.LibrarySyncJob)), ct);
        _log.LogInformation("LibrarySyncService.RequestSyncAsync exit");
    }

    public async Task<IReadOnlyList<LibrarySyncReport>> SyncAsync(CancellationToken ct = default)
    {
        _log.LogInformation("LibrarySyncService.SyncAsync entry");
        var reports = new List<LibrarySyncReport>();
        foreach (var source in Sources)
            reports.Add(await SyncSourceAsync(source, ct));
        _log.LogInformation("LibrarySyncService.SyncAsync exit {Reports}", string.Join("; ", reports.Select(r => $"{r.Source}={r.Outcome}")));
        return reports;
    }

    private async Task<LibrarySyncReport> SyncSourceAsync(string source, CancellationToken ct)
    {
        if (!_source.IsConfigured(source))
            return new LibrarySyncReport(source, "not_configured", null, 0, 0, 0, []);

        var now = _clock.GetUtcNow().UtcDateTime;
        var state = await _db.LibrarySyncStates.FirstOrDefaultAsync(s => s.Source == source, ct);
        if (state is null)
        {
            state = new LibrarySyncState { Source = source };
            _db.LibrarySyncStates.Add(state);
        }
        state.LastAttemptAt = now;

        LibrarySnapshot snapshot;
        try
        {
            snapshot = await _source.FetchAsync(source, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "LibrarySyncService.SyncSourceAsync error source={Source}: serving the last known state", source);
            await RecordFailureAsync(state, source, ex.Message, now, ct);
            return new LibrarySyncReport(source, "failed", state.LastCommitSha, 0, 0, 0, [], ex.Message);
        }

        state.Repository = snapshot.Repository;
        if (snapshot.CommitSha == state.LastCommitSha)
        {
            RecordSuccess(state, snapshot.CommitSha, now);
            await _db.SaveChangesAsync(ct);
            return new LibrarySyncReport(source, "unchanged", snapshot.CommitSha, 0, 0, 0, []);
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var report = source == "connectors"
            ? await ApplyConnectorsAsync(snapshot, now, ct)
            : await ApplyTemplatesAsync(snapshot, now, ct);
        RecordSuccess(state, snapshot.CommitSha, now);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return report;
    }

    private static void RecordSuccess(LibrarySyncState state, string sha, DateTime now)
    {
        state.LastCommitSha = sha;
        state.LastSuccessAt = now;
        state.ConsecutiveFailures = 0;
        state.LastError = null;
        state.AlertedAt = null;
    }

    private async Task RecordFailureAsync(LibrarySyncState state, string source, string error, DateTime now, CancellationToken ct)
    {
        state.ConsecutiveFailures += 1;
        state.LastError = error.Length > 2000 ? error[..2000] : error;
        if (state.ConsecutiveFailures >= AlertAfterFailures && state.AlertedAt is null)
        {
            state.AlertedAt = now;
            _log.LogError("Library sync for {Source} has failed {Count} times in a row: {Error}", source, state.ConsecutiveFailures, state.LastError);
            await NotifyStaffAsync(
                $"Library sync is failing ({source})",
                $"The {source} Library hasn't synced from GitHub {state.ConsecutiveFailures} times in a row, so it is serving the state from " +
                $"{(state.LastSuccessAt is { } at ? at.ToString("yyyy-MM-dd HH:mm") + " UTC" : "before the first sync")}. Last error: {state.LastError} " +
                "Check the repository, Library:GitHubToken and GitHub's status, then run a sync from Platform → Library.",
                ct);
        }
        await _db.SaveChangesAsync(ct);
    }

    // ── Connectors ──────────────────────────────────────────────────────────────────

    private async Task<LibrarySyncReport> ApplyConnectorsAsync(LibrarySnapshot snapshot, DateTime now, CancellationToken ct)
    {
        var skipped = new List<string>();
        var parsed = new List<ParsedConnector>();
        foreach (var item in snapshot.Items)
        {
            try
            {
                if (!item.Files.TryGetValue("connector.yaml", out var manifest)) continue;
                var connector = LibraryManifestParser.ParseConnector(new LibraryFolder(item.Path, manifest, item.Files.GetValueOrDefault("CHANGELOG.md")));
                if (connector.PublisherId == LibraryManifestParser.OfficialPublisher && OfficialIds.Contains(connector.ConnectorId))
                {
                    skipped.Add($"{item.Path}: official connectors ship with the platform release, not the Library sync.");
                    continue;
                }
                parsed.Add(connector);
            }
            catch (LibraryManifestException ex)
            {
                skipped.Add(ex.Message);
            }
        }
        // Two folders claiming the same publisher/id: keep the first, report the rest.
        parsed = parsed.GroupBy(p => (p.PublisherId, p.ConnectorId, p.MajorVersion))
            .Select(g =>
            {
                foreach (var dup in g.Skip(1)) skipped.Add($"{dup.Path}: {dup.PublisherId}/{dup.ConnectorId}@{dup.MajorVersion} is already defined in {g.First().Path}.");
                return g.First();
            }).ToList();

        // Only platform-wide community rows belong to the sync.
        var existing = await _db.ConnectorDefinitions
            .Where(c => c.WorkspaceId == null && !c.IsOfficial && c.Tier == ConnectorTier.Community)
            .ToListAsync(ct);
        var byKey = existing.ToDictionary(c => (c.PublisherId, c.ConnectorId, c.MajorVersion));

        int added = 0, updated = 0, deprecated = 0;
        foreach (var p in parsed)
        {
            if (byKey.TryGetValue((p.PublisherId, p.ConnectorId, p.MajorVersion), out var row))
            {
                var wasDeprecated = row.DeprecatedAt is not null;
                if (SemVersion.IsNewer(p.Version, row.Version))
                {
                    var from = row.Version;
                    Apply(row, p, now);
                    updated++;
                    await NotifyMinorUpdateAsync(row, from, ct);
                }
                // Deprecation follows the manifest (and a connector that came back un-deprecates).
                if (p.Deprecated && !wasDeprecated)
                {
                    row.DeprecatedAt = now;
                    row.DeprecationNote = p.DeprecationNote;
                    deprecated++;
                    await NotifyDeprecatedAsync(row, ct);
                }
                else if (!p.Deprecated && wasDeprecated)
                {
                    row.DeprecatedAt = null;
                    row.DeprecationNote = null;
                }
                continue;
            }

            var created = new ConnectorDefinition
            {
                ConnectorId = p.ConnectorId,
                PublisherId = p.PublisherId,
                Tier = ConnectorTier.Community,
                IsOfficial = false,
                IsEnabled = true,
                WorkspaceId = null,
                MajorVersion = p.MajorVersion,
            };
            Apply(created, p, now);
            if (p.Deprecated) { created.DeprecatedAt = now; created.DeprecationNote = p.DeprecationNote; }
            _db.ConnectorDefinitions.Add(created);
            existing.Add(created);
            added++;
        }

        // Gone from the repository: a 90-day notice, once. An older major whose connector is still there (the folder
        // now holds 2.x) isn't gone, only superseded: it keeps running, pinned, without a notice.
        var present = parsed.Select(p => (p.PublisherId, p.ConnectorId, p.MajorVersion)).ToHashSet();
        var stillListed = parsed.Select(p => (p.PublisherId, p.ConnectorId)).ToHashSet();
        foreach (var row in existing.Where(r => r.DeprecatedAt is null
                                                && !present.Contains((r.PublisherId, r.ConnectorId, r.MajorVersion))
                                                && !stillListed.Contains((r.PublisherId, r.ConnectorId))))
        {
            row.DeprecatedAt = now;
            row.DeprecationNote = RemovedNote;
            deprecated++;
            await NotifyDeprecatedAsync(row, ct);
        }

        // Latest major per connector; workspaces on an older major hear about the new one once, when it lands.
        foreach (var group in existing.GroupBy(r => (r.PublisherId, r.ConnectorId)))
        {
            var latest = group.Where(r => r.DeprecatedAt is null).DefaultIfEmpty(group.MaxBy(r => r.MajorVersion)).MaxBy(r => r!.MajorVersion)!;
            foreach (var row in group)
            {
                var shouldBeLatest = row == latest;
                if (row.IsLatestMajor && !shouldBeLatest && row.MajorVersion < latest.MajorVersion)
                    await NotifyMajorAvailableAsync(row, latest, ct);
                row.IsLatestMajor = shouldBeLatest;
            }
        }

        _log.LogInformation("LibrarySyncService connectors sha={Sha} added={Added} updated={Updated} deprecated={Deprecated} skipped={Skipped}",
            snapshot.CommitSha, added, updated, deprecated, skipped.Count);
        return new LibrarySyncReport("connectors", "synced", snapshot.CommitSha, added, updated, deprecated, skipped);
    }

    private static readonly HashSet<string> OfficialIds =
        Flowamaz.Application.Connectors.OfficialConnectorCatalog.Connectors.Select(c => c.ConnectorId).ToHashSet();

    private static void Apply(ConnectorDefinition row, ParsedConnector p, DateTime now)
    {
        row.DisplayName = p.DisplayName;
        row.Version = p.Version;
        row.Category = p.Category;
        row.Tags = p.Tags;
        row.ManifestJson = p.ManifestJson;
        row.ChangelogJson = JsonSerializer.Serialize(p.Changelog);
        row.SourceUrl = $"library://{p.Path}";
        row.LastUpdatedAt = now;
    }

    private async Task NotifyMinorUpdateAsync(ConnectorDefinition row, string fromVersion, CancellationToken ct)
    {
        var installs = await _db.WorkspaceConnectors.Where(wc => wc.ConnectorDefinitionId == row.Id).ToListAsync(ct);
        var notes = LibraryManifestParser.Between(
            JsonSerializer.Deserialize<List<ChangelogEntry>>(row.ChangelogJson) ?? [], fromVersion, row.Version);
        var summary = notes.Count == 0 ? "No release notes were published." : string.Join(" ", notes.Select(n => $"{n.Version}: {n.Notes}"));
        foreach (var install in installs)
        {
            var from = string.IsNullOrEmpty(install.InstalledVersion) ? fromVersion : install.InstalledVersion;
            install.InstalledVersion = row.Version;
            await NotifyAdminsAsync(install.WorkspaceId, "connector_updated",
                $"{row.DisplayName} updated to {row.Version}",
                $"{row.DisplayName} moved from {from} to {row.Version} automatically ({row.PublisherId}/{row.ConnectorId}@{row.MajorVersion}). {Trim(summary)}",
                $"/library/connectors/{row.ConnectorId}", ct);
        }
    }

    private async Task NotifyMajorAvailableAsync(ConnectorDefinition pinned, ConnectorDefinition latest, CancellationToken ct)
    {
        var installs = await _db.WorkspaceConnectors.Where(wc => wc.ConnectorDefinitionId == pinned.Id).Select(wc => wc.WorkspaceId).ToListAsync(ct);
        foreach (var workspaceId in installs)
            await NotifyAdminsAsync(workspaceId, "connector_major_available",
                $"{latest.DisplayName} {latest.Version} is available",
                $"This workspace runs {pinned.DisplayName} {pinned.Version} (pinned to @{pinned.MajorVersion}). Version {latest.Version} is a new major " +
                "release and may change how actions behave, so it isn't applied automatically. Review the changes and upgrade from Library → Connectors.",
                $"/library/connectors/{latest.ConnectorId}", ct);
    }

    private async Task NotifyDeprecatedAsync(ConnectorDefinition row, CancellationToken ct)
    {
        var installs = await _db.WorkspaceConnectors.Where(wc => wc.ConnectorDefinitionId == row.Id).Select(wc => wc.WorkspaceId).ToListAsync(ct);
        foreach (var workspaceId in installs)
            await NotifyAdminsAsync(workspaceId, "connector_deprecated",
                $"{row.DisplayName} is deprecated",
                $"{row.DeprecationNote} It keeps working here, but leaves the Library on {row.RemovalAt:yyyy-MM-dd}. " +
                "Move the workflows that use it to another connector before then.",
                $"/library/connectors/{row.ConnectorId}", ct);
    }

    // ── Templates ───────────────────────────────────────────────────────────────────

    private async Task<LibrarySyncReport> ApplyTemplatesAsync(LibrarySnapshot snapshot, DateTime now, CancellationToken ct)
    {
        var skipped = new List<string>();
        var parsed = new List<ParsedTemplate>();
        foreach (var item in snapshot.Items)
        {
            try
            {
                if (!item.Files.TryGetValue("template.yaml", out var manifest)) continue;
                parsed.Add(LibraryManifestParser.ParseTemplate(new LibraryFolder(item.Path, manifest, null, item.Files.GetValueOrDefault("workflow.yaml"))));
            }
            catch (LibraryManifestException ex)
            {
                skipped.Add(ex.Message);
            }
        }

        var slugs = parsed.Select(p => p.Slug).ToList();
        var bySlug = await _db.WorkflowTemplates.IgnoreQueryFilters()
            .Where(t => slugs.Contains(t.Slug) || t.PublisherId != null)
            .ToListAsync(ct);
        int added = 0, updated = 0, deprecated = 0;
        foreach (var p in parsed)
        {
            var row = bySlug.FirstOrDefault(t => t.Slug == p.Slug);
            // A slug already used by a template published from a workspace isn't the Library's to overwrite.
            if (row is not null && row.PublisherId is null && !row.IsOfficial)
            {
                skipped.Add($"{p.Path}: the slug '{p.Slug}' belongs to a community template published from a workspace.");
                continue;
            }
            if (row is null)
            {
                row = new Flowamaz.Core.Entities.Library.WorkflowTemplate { Slug = p.Slug, ReviewStatus = "approved", IsActive = true };
                _db.WorkflowTemplates.Add(row);
                bySlug.Add(row);
                added++;
            }
            else if (SemVersion.IsNewer(p.Version, row.Version) || row.IsDeleted) updated++;
            else
            {
                SyncDeprecation(row, p, now, ref deprecated);
                continue;
            }

            row.Name = p.Name;
            row.Description = p.Description;
            row.Category = p.Category;
            row.Tags = p.Tags;
            row.Version = p.Version;
            row.YamlContent = p.WorkflowYaml;
            row.PublisherId = p.PublisherId;
            row.IsOfficial = p.PublisherId == LibraryManifestParser.OfficialPublisher;
            row.IsDeleted = false;
            row.DeletedAt = null;
            row.IsActive = true;
            SyncDeprecation(row, p, now, ref deprecated);
        }

        var present = parsed.Select(p => p.Slug).ToHashSet();
        foreach (var row in bySlug.Where(t => t.PublisherId != null && t.DeprecatedAt is null && !present.Contains(t.Slug)))
        {
            row.DeprecatedAt = now;
            row.DeprecationNote = RemovedNote;
            deprecated++;
        }

        _log.LogInformation("LibrarySyncService templates sha={Sha} added={Added} updated={Updated} deprecated={Deprecated} skipped={Skipped}",
            snapshot.CommitSha, added, updated, deprecated, skipped.Count);
        return new LibrarySyncReport("templates", "synced", snapshot.CommitSha, added, updated, deprecated, skipped);
    }

    private static void SyncDeprecation(Flowamaz.Core.Entities.Library.WorkflowTemplate row, ParsedTemplate p, DateTime now, ref int deprecated)
    {
        if (p.Deprecated && row.DeprecatedAt is null)
        {
            row.DeprecatedAt = now;
            row.DeprecationNote = p.DeprecationNote;
            deprecated++;
        }
        else if (!p.Deprecated && row.DeprecatedAt is not null)
        {
            row.DeprecatedAt = null;
            row.DeprecationNote = null;
        }
    }

    // ── Notifications ───────────────────────────────────────────────────────────────

    private async Task NotifyAdminsAsync(Guid workspaceId, string type, string title, string message, string actionUrl, CancellationToken ct)
    {
        var orgId = await _db.Workspaces.Where(w => w.Id == workspaceId).Select(w => w.OrgId).FirstOrDefaultAsync(ct);
        var admins = await _db.WorkspaceMembers
            .Where(m => m.WorkspaceId == workspaceId && m.IsActive && m.Role == WorkspaceRole.Admin)
            .Select(m => m.OrgUserId)
            .ToListAsync(ct);
        foreach (var userId in admins.Distinct())
            _db.Notifications.Add(new Notification
            {
                UserId = userId, OrgId = orgId, WorkspaceId = workspaceId,
                Type = type, Title = Trim(title, 200), Message = Trim(message), ActionUrl = actionUrl,
            });
    }

    private async Task NotifyStaffAsync(string title, string message, CancellationToken ct)
    {
        var staffOrgs = PlatformStaff.StaffOrgIds(_config);
        if (staffOrgs.Count == 0)
        {
            _log.LogError("Library sync alert has no recipient: set {Key}. {Title}: {Message}", PlatformStaff.ConfigKey, title, message);
            return;
        }
        var owners = await _db.OrgUsers.Where(u => staffOrgs.Contains(u.OrgId) && u.IsOrgOwner && u.IsActive)
            .Select(u => new { u.Id, u.OrgId }).ToListAsync(ct);
        foreach (var owner in owners)
            _db.Notifications.Add(new Notification
            {
                UserId = owner.Id, OrgId = owner.OrgId, Type = "library_sync_failing",
                Title = Trim(title, 200), Message = Trim(message), ActionUrl = "/platform",
            });
    }

    private static string Trim(string text, int max = 1000) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
