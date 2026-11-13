using System.Text.Json;
using System.Text.RegularExpressions;
using Flowamaz.Application.Library;
using Flowamaz.Application.Library.Services;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Entities.Library;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Library;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YamlDotNet.RepresentationModel;

namespace Flowamaz.Infrastructure.Services.Library;

/// <summary>
/// The Library for one workspace (FUNCTIONAL.md §23): what it can install, what it runs, what needs an upgrade.
/// Every query is limited to platform-wide items and this workspace's own (rule 1: workspace isolation).
/// </summary>
public sealed partial class LibraryService : ILibraryService
{
    private readonly FlowAmazDbContext _db;
    private readonly IConnectorCatalogueService _catalogue;
    private readonly ITemplateService _templates;
    private readonly ConnectorMarketplaceService _marketplace;
    private readonly IWorkflowValidator _validator;
    private readonly ILogger<LibraryService> _log;
    private readonly IAuditService? _audit;
    private readonly TimeProvider _clock;

    public LibraryService(
        FlowAmazDbContext db,
        IConnectorCatalogueService catalogue,
        ITemplateService templates,
        ConnectorMarketplaceService marketplace,
        IWorkflowValidator validator,
        ILogger<LibraryService> log,
        IAuditService? audit = null,
        TimeProvider? clock = null)
    {
        _db = db;
        _catalogue = catalogue;
        _templates = templates;
        _marketplace = marketplace;
        _validator = validator;
        _log = log;
        _audit = audit;
        _clock = clock ?? TimeProvider.System;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    // ── Connectors ──────────────────────────────────────────────────────────────────

    /// <summary>Connector rows this workspace may see: platform-wide ones and its own private ones.</summary>
    private IQueryable<ConnectorDefinition> Visible(Guid workspaceId) =>
        _db.ConnectorDefinitions.AsNoTracking().Where(c => c.IsEnabled && (c.WorkspaceId == null || c.WorkspaceId == workspaceId));

    private bool Removed(ConnectorDefinition c) => c.RemovalAt is { } at && at <= Now;

    public async Task<IReadOnlyList<LibraryConnectorItem>> ListConnectorsAsync(Guid workspaceId, string? query, string? category, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.ListConnectorsAsync entry workspaceId={WorkspaceId} query={Query} category={Category}", workspaceId, query, category);
        var rows = await Visible(workspaceId).ToListAsync(ct);
        var installed = await InstalledAsync(workspaceId, ct);

        var items = rows.GroupBy(r => (r.PublisherId, r.ConnectorId))
            .Select(g =>
            {
                var mine = installed.FirstOrDefault(i => g.Any(r => r.Id == i.ConnectorDefinitionId));
                var latest = Latest(g);
                if (latest is null && mine is null) return null; // removed, and not installed here
                return ToItem(latest ?? g.First(r => r.Id == mine!.ConnectorDefinitionId), mine, g);
            })
            .Where(i => i is not null).Select(i => i!)
            .Where(i => Matches(i, query) && (string.IsNullOrWhiteSpace(category) || i.Category.Equals(category, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(i => i.IsOfficial).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _log.LogInformation("LibraryService.ListConnectorsAsync exit count={Count}", items.Count);
        return items;
    }

    public async Task<LibraryConnectorDetail> GetConnectorAsync(Guid workspaceId, string connectorId, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.GetConnectorAsync entry workspaceId={WorkspaceId} connector={Connector}", workspaceId, connectorId);
        var group = await ResolveAsync(workspaceId, connectorId, ct);
        var installed = await InstalledAsync(workspaceId, ct);
        var mine = installed.FirstOrDefault(i => group.Any(r => r.Id == i.ConnectorDefinitionId));
        var shown = Latest(group) ?? group.First(r => r.Id == mine?.ConnectorDefinitionId);
        var detail = new LibraryConnectorDetail(
            ToItem(shown, mine, group),
            Operations(shown.ManifestJson),
            Changelog(shown),
            group.Sum(r => r.InstallCount),
            shown.AverageRating);
        _log.LogInformation("LibraryService.GetConnectorAsync exit connector={Connector} version={Version}", connectorId, shown.Version);
        return detail;
    }

    public async Task<LibraryConnectorItem> InstallConnectorAsync(Guid workspaceId, string connectorId, Guid userId, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.InstallConnectorAsync entry workspaceId={WorkspaceId} connector={Connector}", workspaceId, connectorId);
        var group = await ResolveAsync(workspaceId, connectorId, ct);
        var installed = await InstalledAsync(workspaceId, ct);
        var latest = Latest(group);
        if (installed.FirstOrDefault(i => group.Any(r => r.Id == i.ConnectorDefinitionId)) is { } existing)
        {
            var current = group.First(r => r.Id == existing.ConnectorDefinitionId);
            throw LibraryException.AlreadyInstalled(current.DisplayName, Pin(current), latest is not null && latest.MajorVersion > current.MajorVersion);
        }
        if (latest is null)
        {
            var newest = group.MaxBy(r => r.MajorVersion)!;
            throw LibraryException.Removed(newest.DisplayName, newest.RemovalAt);
        }

        var wc = await _catalogue.InstallAsync(workspaceId, latest.Id, null, userId, ct);
        _log.LogInformation("LibraryService.InstallConnectorAsync exit workspaceId={WorkspaceId} pin={Pin}", workspaceId, Pin(latest));
        return ToItem(latest, wc, group);
    }

    public async Task<IReadOnlyList<LibraryOutdatedItem>> GetOutdatedConnectorsAsync(Guid workspaceId, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.GetOutdatedConnectorsAsync entry workspaceId={WorkspaceId}", workspaceId);
        var installed = await InstalledAsync(workspaceId, ct);
        var rows = await Visible(workspaceId).ToListAsync(ct);
        var outdated = new List<LibraryOutdatedItem>();
        foreach (var wc in installed)
        {
            var current = rows.FirstOrDefault(r => r.Id == wc.ConnectorDefinitionId);
            if (current is null) continue;
            var group = rows.Where(r => r.PublisherId == current.PublisherId && r.ConnectorId == current.ConnectorId).ToList();
            var latest = Latest(group);
            var newer = latest is not null && latest.MajorVersion > current.MajorVersion ? latest : null;
            if (newer is null && current.DeprecatedAt is null) continue;
            var installedVersion = string.IsNullOrEmpty(wc.InstalledVersion) ? current.Version : wc.InstalledVersion;
            outdated.Add(new LibraryOutdatedItem(
                current.ConnectorId, current.PublisherId, current.DisplayName, installedVersion, Pin(current),
                newer?.Version,
                newer is null ? [] : LibraryManifestParser.Between(Changelog(newer), installedVersion, newer.Version),
                current.DeprecatedAt, current.RemovalAt, current.DeprecationNote));
        }
        _log.LogInformation("LibraryService.GetOutdatedConnectorsAsync exit count={Count}", outdated.Count);
        return outdated;
    }

    public async Task<LibraryUpdateResult> UpgradeConnectorAsync(Guid workspaceId, string connectorId, Guid userId, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.UpgradeConnectorAsync entry workspaceId={WorkspaceId} connector={Connector}", workspaceId, connectorId);
        var group = await ResolveAsync(workspaceId, connectorId, ct);
        var ids = group.Select(r => r.Id).ToList();
        var wc = await _db.WorkspaceConnectors.FirstOrDefaultAsync(w => w.WorkspaceId == workspaceId && ids.Contains(w.ConnectorDefinitionId), ct)
                 ?? throw LibraryException.NotInstalled(connectorId);
        var current = group.First(r => r.Id == wc.ConnectorDefinitionId);
        var latest = Latest(group);
        if (latest is null || latest.MajorVersion <= current.MajorVersion) throw LibraryException.UpToDate(current.DisplayName, Pin(current));

        var from = string.IsNullOrEmpty(wc.InstalledVersion) ? current.Version : wc.InstalledVersion;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        wc.ConnectorDefinitionId = latest.Id;
        wc.InstalledVersion = latest.Version;
        wc.UpdatedAt = Now;
        await _db.ConnectorDefinitions.Where(c => c.Id == current.Id && c.InstallCount > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.InstallCount, c => c.InstallCount - 1), ct);
        await _db.ConnectorDefinitions.Where(c => c.Id == latest.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.InstallCount, c => c.InstallCount + 1), ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _audit?.RecordAsync(new AuditEventRequest
        {
            WorkspaceId = workspaceId,
            ActorUserId = userId,
            ActorType = "user",
            EventType = "connector.upgraded",
            ResourceType = "connector",
            ResourceId = latest.Id,
            ResourceLabel = latest.DisplayName,
            Action = "upgraded",
            Metadata = new { from, to = latest.Version, pin = Pin(latest) },
        }, ct);
        _log.LogInformation("LibraryService.UpgradeConnectorAsync exit workspaceId={WorkspaceId} from={From} to={To}", workspaceId, from, latest.Version);
        return new LibraryUpdateResult(latest.ConnectorId, from, latest.Version, Pin(latest));
    }

    /// <summary>All visible rows of one connector, by id or publisher/id.</summary>
    private async Task<List<ConnectorDefinition>> ResolveAsync(Guid workspaceId, string reference, CancellationToken ct)
    {
        var parts = reference.Trim().Split('/', 2);
        var (publisher, id) = parts.Length == 2 ? (parts[0], parts[1].Split('@')[0]) : ((string?)null, parts[0].Split('@')[0]);
        var rows = await Visible(workspaceId).Where(c => c.ConnectorId == id && (publisher == null || c.PublisherId == publisher)).ToListAsync(ct);
        if (rows.Count == 0) throw LibraryException.ConnectorNotFound(reference);
        var publishers = rows.Select(r => r.PublisherId).Distinct().ToList();
        if (publishers.Count > 1) throw LibraryException.Ambiguous(id, publishers.Select(p => $"{p}/{id}"));
        return rows;
    }

    private Task<List<WorkspaceConnector>> InstalledAsync(Guid workspaceId, CancellationToken ct) =>
        _db.WorkspaceConnectors.AsNoTracking().Where(w => w.WorkspaceId == workspaceId).ToListAsync(ct);

    /// <summary>The row offered to new installs: the latest major still in the Library; null when all were removed.</summary>
    private ConnectorDefinition? Latest(IEnumerable<ConnectorDefinition> group) =>
        group.Where(r => !Removed(r)).OrderByDescending(r => r.IsLatestMajor).ThenByDescending(r => r.MajorVersion).FirstOrDefault();

    private LibraryConnectorItem ToItem(ConnectorDefinition shown, WorkspaceConnector? mine, IEnumerable<ConnectorDefinition> group)
    {
        var installedRow = mine is null ? null : group.FirstOrDefault(r => r.Id == mine.ConnectorDefinitionId);
        return new LibraryConnectorItem(
            shown.ConnectorId, shown.PublisherId, shown.DisplayName, shown.Version, shown.Category, shown.Tags, shown.IsOfficial,
            Pin(shown), mine is not null,
            installedRow is null ? null : string.IsNullOrEmpty(mine!.InstalledVersion) ? installedRow.Version : mine.InstalledVersion,
            installedRow is null ? null : Pin(installedRow),
            (installedRow ?? shown).DeprecatedAt, (installedRow ?? shown).RemovalAt, (installedRow ?? shown).DeprecationNote);
    }

    internal static string Pin(ConnectorDefinition c) => $"{c.PublisherId}/{c.ConnectorId}@{c.MajorVersion}";

    private static bool Matches(LibraryConnectorItem i, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word =>
            i.Id.Contains(word, StringComparison.OrdinalIgnoreCase) || i.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
            || i.Publisher.Contains(word, StringComparison.OrdinalIgnoreCase) || i.Category.Contains(word, StringComparison.OrdinalIgnoreCase)
            || i.Tags.Any(t => t.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<string> Operations(string manifestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(manifestJson);
            if (!doc.RootElement.TryGetProperty("operations", out var ops) || ops.ValueKind != JsonValueKind.Array) return [];
            return ops.EnumerateArray()
                .Select(o => o.ValueKind == JsonValueKind.Object && o.TryGetProperty("id", out var id) ? id.GetString() : o.ValueKind == JsonValueKind.String ? o.GetString() : null)
                .Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<ChangelogEntry> Changelog(ConnectorDefinition c)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ChangelogEntry>>(c.ChangelogJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ── Templates ───────────────────────────────────────────────────────────────────

    private IQueryable<WorkflowTemplate> OfferedTemplates() =>
        _db.WorkflowTemplates.AsNoTracking().Where(t => t.IsActive && t.ReviewStatus == "approved");

    public async Task<IReadOnlyList<LibraryTemplateItem>> ListTemplatesAsync(Guid workspaceId, string? query, string? category, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.ListTemplatesAsync entry workspaceId={WorkspaceId} query={Query}", workspaceId, query);
        var rows = await OfferedTemplates().ToListAsync(ct);
        var cutoff = Now.AddDays(-ConnectorDefinition.DeprecationNoticeDays);
        var items = rows
            .Where(t => t.DeprecatedAt is null || t.DeprecatedAt > cutoff)
            .Where(t => string.IsNullOrWhiteSpace(category) || t.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Where(t => string.IsNullOrWhiteSpace(query) || query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w =>
                t.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || t.Slug.Contains(w, StringComparison.OrdinalIgnoreCase)
                || t.Description.Contains(w, StringComparison.OrdinalIgnoreCase) || t.Tags.Any(x => x.Contains(w, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(t => t.IsOfficial).ThenByDescending(t => t.InstallCount).ThenBy(t => t.Name)
            .Select(ToItem)
            .ToList();
        _log.LogInformation("LibraryService.ListTemplatesAsync exit count={Count}", items.Count);
        return items;
    }

    public async Task<LibraryTemplateItem> GetTemplateAsync(Guid workspaceId, string slugOrId, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.GetTemplateAsync entry workspaceId={WorkspaceId} template={Template}", workspaceId, slugOrId);
        var item = ToItem(await FindTemplateAsync(slugOrId, ct));
        _log.LogInformation("LibraryService.GetTemplateAsync exit template={Template}", item.Slug);
        return item;
    }

    public async Task<Guid> InstallTemplateAsync(Guid workspaceId, string slugOrId, string? name, Guid userId, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.InstallTemplateAsync entry workspaceId={WorkspaceId} template={Template}", workspaceId, slugOrId);
        var template = await FindTemplateAsync(slugOrId, ct);
        if (template.DeprecatedAt is { } deprecated && deprecated.AddDays(ConnectorDefinition.DeprecationNoticeDays) <= Now)
            throw LibraryException.Removed(template.Name, deprecated.AddDays(ConnectorDefinition.DeprecationNoticeDays));
        var workflowId = await _templates.InstallTemplateAsync(template.Id, workspaceId, userId, string.IsNullOrWhiteSpace(name) ? template.Name : name, ct);
        _log.LogInformation("LibraryService.InstallTemplateAsync exit workflowId={WorkflowId}", workflowId);
        return workflowId;
    }

    private async Task<WorkflowTemplate> FindTemplateAsync(string slugOrId, CancellationToken ct)
    {
        var key = slugOrId.Trim();
        var byId = Guid.TryParse(key, out var id);
        return await OfferedTemplates().FirstOrDefaultAsync(t => byId ? t.Id == id : t.Slug == key, ct)
               ?? throw LibraryException.TemplateNotFound(slugOrId);
    }

    private static LibraryTemplateItem ToItem(WorkflowTemplate t) => new(
        t.Id, t.Slug, t.Name, t.Description, t.Category, t.Version, t.PublisherId, t.IsOfficial, t.Tags, t.InstallCount,
        t.DeprecatedAt, t.DeprecatedAt?.AddDays(ConnectorDefinition.DeprecationNoticeDays));

    // ── Publishing ──────────────────────────────────────────────────────────────────

    public async Task<LibraryPublishResult> PublishConnectorAsync(Guid workspaceId, Guid orgId, string manifestYaml, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.PublishConnectorAsync entry workspaceId={WorkspaceId}", workspaceId);
        var id = LibraryManifestParser.DeclaredId(manifestYaml)
                 ?? throw LibraryException.InvalidManifest("connector.yaml needs an id: lowercase letters, numbers and hyphens (e.g. my-connector).");
        ConnectorSubmissionResult submitted;
        try
        {
            submitted = await _marketplace.SubmitConnectorAsync(orgId, id, manifestYaml, ct);
        }
        catch (InvalidOperationException ex)
        {
            throw LibraryException.InvalidManifest(ex.Message.Replace(" Fix the manifest and submit again.", ""));
        }
        _log.LogInformation("LibraryService.PublishConnectorAsync exit pr={Pr}", submitted.GithubPrUrl);
        return new LibraryPublishResult("connector", id, submitted.Status,
            "Submitted for review as a pull request. Once it merges, the Library sync makes it live (within 15 minutes).",
            submitted.GithubPrUrl);
    }

    public async Task<LibraryPublishResult> PublishTemplateAsync(Guid workspaceId, Guid orgId, Guid userId, string manifestYaml, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.PublishTemplateAsync entry workspaceId={WorkspaceId}", workspaceId);
        var (name, version, description, category, tags, workflowYaml) = ReadTemplateManifest(manifestYaml);

        var validation = await _validator.ValidateAsync(workflowYaml, workspaceId, ct);
        if (!validation.IsValid)
            throw LibraryException.InvalidManifest($"The template's workflow doesn't validate: {validation.Errors[0].Message}");

        var slugBase = SlugRegex().Replace(name.ToLowerInvariant(), "-").Trim('-') is { Length: > 0 } s ? s : "template";
        var slug = slugBase;
        for (var n = 2; await _db.WorkflowTemplates.IgnoreQueryFilters().AnyAsync(t => t.Slug == slug, ct); n++) slug = $"{slugBase}-{n}";

        var template = new WorkflowTemplate
        {
            Name = name, Slug = slug, Description = description, Category = category, Tags = tags, Version = version,
            YamlContent = workflowYaml, IsOfficial = false, OrgId = orgId, IsActive = true, ReviewStatus = "pending", CreatedBy = userId,
        };
        _db.WorkflowTemplates.Add(template);
        await _db.SaveChangesAsync(ct);

        _audit?.RecordAsync(new AuditEventRequest
        {
            OrgId = orgId, WorkspaceId = workspaceId, ActorUserId = userId, ActorType = "user",
            EventType = "template.published", ResourceType = "workflow_template", ResourceId = template.Id,
            ResourceLabel = template.Name, Action = "published",
        }, ct);
        _log.LogInformation("LibraryService.PublishTemplateAsync exit template={Slug}", slug);
        return new LibraryPublishResult("template", slug, "pending",
            "Submitted for review. It appears in the Library once a reviewer approves it.");
    }

    /// <summary>template.yaml as `fmz template init` writes it: name, version, description, category, tags, workflow.</summary>
    internal static (string Name, string Version, string Description, string Category, string[] Tags, string WorkflowYaml) ReadTemplateManifest(string manifestYaml)
    {
        if (string.IsNullOrWhiteSpace(manifestYaml)) throw LibraryException.InvalidManifest("template.yaml is empty.");
        YamlMappingNode root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(manifestYaml));
            root = stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode
                   ?? throw LibraryException.InvalidManifest("template.yaml must be a YAML mapping (name, version, workflow).");
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw LibraryException.InvalidManifest($"template.yaml isn't valid YAML (line {ex.Start.Line}): {ex.Message}.");
        }

        string? Get(string key) => root.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s && !string.IsNullOrWhiteSpace(s.Value) ? s.Value!.Trim() : null;
        var name = Get("name") ?? throw LibraryException.InvalidManifest("template.yaml needs a name.");
        if (name.Length > 120) throw LibraryException.InvalidManifest("The template name is longer than 120 characters.");
        var version = Get("version") ?? "1.0.0";
        if (!SemVersion.TryParse(version, out var semver)) throw LibraryException.InvalidManifest($"version '{version}' isn't semver (e.g. 1.0.0).");
        var tags = root.Children.TryGetValue(new YamlScalarNode("tags"), out var t) && t is YamlSequenceNode seq
            ? seq.Children.OfType<YamlScalarNode>().Select(x => x.Value?.Trim()).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Take(20).ToArray()
            : [];

        string workflowYaml;
        if (Get("workflow_yaml") is { } literal) workflowYaml = literal;
        else if (root.Children.TryGetValue(new YamlScalarNode("workflow"), out var wf) && wf is YamlMappingNode { Children.Count: > 0 } map)
        {
            var doc = new YamlStream(new YamlDocument(map));
            using var writer = new StringWriter();
            doc.Save(writer, assignAnchors: false);
            workflowYaml = writer.ToString().TrimEnd('.', '\n', '\r', ' ') + "\n";
        }
        else throw LibraryException.InvalidManifest("template.yaml has no workflow. Put the complete workflow YAML under `workflow:`.");

        var description = Get("description") ?? "";
        return (name, semver.ToString(), description.Length > 1000 ? description[..1000] : description, Get("category") ?? "Custom", tags, workflowYaml);
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugRegex();
}
