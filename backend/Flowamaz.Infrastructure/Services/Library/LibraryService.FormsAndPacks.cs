using System.Text.Json;
using Flowamaz.Core.Entities.Library;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Models;
using Flowamaz.Core.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services.Library;

/// <summary>Library Forms and Packs (FUNCTIONAL.md §23.2).</summary>
public sealed partial class LibraryService
{
    private static readonly JsonSerializerOptions FormJson = new(JsonSerializerDefaults.Web);

    // ── Forms ───────────────────────────────────────────────────────────────────────

    /// <summary>Library forms and this workspace's own (rule 1: never another workspace's).</summary>
    private IQueryable<FormDefinition> VisibleForms(Guid workspaceId) =>
        _db.FormDefinitions.AsNoTracking().Where(f => f.WorkspaceId == null || f.WorkspaceId == workspaceId);

    public async Task<IReadOnlyList<LibraryFormItem>> ListFormsAsync(Guid workspaceId, string? query, string? category, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.ListFormsAsync entry workspaceId={WorkspaceId} query={Query}", workspaceId, query);
        var cutoff = Now.AddDays(-ConnectorDefinitionNoticeDays);
        var rows = await VisibleForms(workspaceId).ToListAsync(ct);
        var items = rows
            .Where(f => f.DeprecatedAt is null || f.DeprecatedAt > cutoff)
            .Where(f => string.IsNullOrWhiteSpace(category) || f.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Where(f => Words(query).All(w => f.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || f.Slug.Contains(w, StringComparison.OrdinalIgnoreCase)
                || f.Description.Contains(w, StringComparison.OrdinalIgnoreCase) || f.Tags.Any(t => t.Contains(w, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(f => f.WorkspaceId != null).ThenByDescending(f => f.IsOfficial).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToForm)
            .ToList();
        _log.LogInformation("LibraryService.ListFormsAsync exit count={Count}", items.Count);
        return items;
    }

    public async Task<LibraryFormItem> GetFormAsync(Guid workspaceId, string slugOrId, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.GetFormAsync entry workspaceId={WorkspaceId} form={Form}", workspaceId, slugOrId);
        var form = ToForm(await FindFormAsync(workspaceId, slugOrId, ct));
        _log.LogInformation("LibraryService.GetFormAsync exit form={Form}", form.Slug);
        return form;
    }

    public async Task<LibraryFormItem> SaveFormAsync(Guid workspaceId, string name, string? description, string? category,
        IReadOnlyList<GateFormField> fields, Guid userId, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.SaveFormAsync entry workspaceId={WorkspaceId} fields={Fields}", workspaceId, fields.Count);
        var trimmed = name.Trim();
        if (fields.Count == 0) throw LibraryException.FormInvalid("The form has no fields.");
        var problems = GateForms.DefinitionProblems(fields, "The form");
        if (problems.Count > 0) throw LibraryException.FormInvalid(problems[0]);

        var slug = SlugRegex().Replace(trimmed.ToLowerInvariant(), "-").Trim('-');
        if (slug.Length == 0) slug = "form";
        if (slug.Length > 100) slug = slug[..100].Trim('-');
        if (await _db.FormDefinitions.AnyAsync(f => f.WorkspaceId == workspaceId && (f.Slug == slug || f.Name == trimmed), ct))
            throw LibraryException.FormNameTaken(trimmed);

        var row = new FormDefinition
        {
            WorkspaceId = workspaceId,
            Slug = slug,
            Name = trimmed,
            Description = (description ?? "").Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? "Custom" : category.Trim(),
            FieldsJson = JsonSerializer.Serialize(fields, FormJson),
            CreatedBy = userId,
            UpdatedBy = userId,
        };
        _db.FormDefinitions.Add(row);
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("LibraryService.SaveFormAsync exit workspaceId={WorkspaceId} formId={FormId}", workspaceId, row.Id);
        return ToForm(row);
    }

    public async Task DeleteFormAsync(Guid workspaceId, Guid formId, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.DeleteFormAsync entry workspaceId={WorkspaceId} formId={FormId}", workspaceId, formId);
        var row = await _db.FormDefinitions.FirstOrDefaultAsync(f => f.Id == formId && (f.WorkspaceId == null || f.WorkspaceId == workspaceId), ct)
                  ?? throw LibraryException.FormNotFound(formId.ToString());
        if (row.WorkspaceId != workspaceId) throw LibraryException.FormNotYours();
        // Gates hold their own copy of the fields, so deleting the form changes no workflow.
        row.IsDeleted = true;
        row.DeletedAt = Now;
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("LibraryService.DeleteFormAsync exit formId={FormId}", formId);
    }

    private async Task<FormDefinition> FindFormAsync(Guid workspaceId, string slugOrId, CancellationToken ct)
    {
        var key = slugOrId.Trim();
        var byId = Guid.TryParse(key, out var id);
        var matches = await VisibleForms(workspaceId).Where(f => byId ? f.Id == id : f.Slug == key).ToListAsync(ct);
        // This workspace's own form wins over a Library form with the same slug.
        return matches.OrderByDescending(f => f.WorkspaceId != null).FirstOrDefault() ?? throw LibraryException.FormNotFound(slugOrId);
    }

    private static LibraryFormItem ToForm(FormDefinition f) => new(
        f.Id, f.Slug, f.Name, f.Description, f.Category, f.Version, f.PublisherId, f.IsOfficial, f.WorkspaceId != null,
        Fields(f.FieldsJson), f.Tags, f.DeprecatedAt, f.DeprecationNote);

    private static IReadOnlyList<GateFormField> Fields(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse($"{{\"form\":{{\"fields\":{json}}}}}");
            return GateForms.Read(doc.RootElement);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ── Packs ───────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<LibraryPackItem>> ListPacksAsync(Guid workspaceId, string? query, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.ListPacksAsync entry workspaceId={WorkspaceId} query={Query}", workspaceId, query);
        var cutoff = Now.AddDays(-ConnectorDefinitionNoticeDays);
        var rows = (await _db.PackDefinitions.AsNoTracking().ToListAsync(ct))
            .Where(p => p.DeprecatedAt is null || p.DeprecatedAt > cutoff)
            .Where(p => Words(query).All(w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || p.Slug.Contains(w, StringComparison.OrdinalIgnoreCase)
                || p.Description.Contains(w, StringComparison.OrdinalIgnoreCase) || p.Tags.Any(t => t.Contains(w, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(p => p.IsOfficial).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var context = await PackContextAsync(workspaceId, ct);
        var items = rows.Select(p => ToPack(p, context)).ToList();
        _log.LogInformation("LibraryService.ListPacksAsync exit count={Count}", items.Count);
        return items;
    }

    public async Task<LibraryPackItem> GetPackAsync(Guid workspaceId, string slugOrId, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.GetPackAsync entry workspaceId={WorkspaceId} pack={Pack}", workspaceId, slugOrId);
        var pack = ToPack(await FindPackAsync(slugOrId, ct), await PackContextAsync(workspaceId, ct));
        _log.LogInformation("LibraryService.GetPackAsync exit pack={Pack} items={Items}", pack.Slug, pack.Items.Count);
        return pack;
    }

    public async Task<LibraryPackInstallResult> InstallPackAsync(Guid workspaceId, string slugOrId, Guid userId, CancellationToken ct = default)
    {
        _log.LogInformation("LibraryService.InstallPackAsync entry workspaceId={WorkspaceId} pack={Pack}", workspaceId, slugOrId);
        var pack = await FindPackAsync(slugOrId, ct);
        if (pack.DeprecatedAt is { } deprecated && deprecated.AddDays(ConnectorDefinitionNoticeDays) <= Now)
            throw LibraryException.Removed(pack.Name, deprecated.AddDays(ConnectorDefinitionNoticeDays));
        var results = new List<LibraryPackItemResult>();

        foreach (var connectorId in pack.Connectors)
        {
            try
            {
                var item = await InstallConnectorAsync(workspaceId, connectorId, userId, ct);
                results.Add(new("connector", connectorId, item.Name, "installed", $"Installed {item.Name} ({item.Pin})."));
            }
            catch (LibraryException ex) when (ex.ErrorCode == "LIBRARY_CONNECTOR_INSTALLED")
            {
                results.Add(new("connector", connectorId, connectorId, "already_installed", ex.Message));
            }
            catch (AppException ex)
            {
                _log.LogWarning("LibraryService.InstallPackAsync connector {Connector} not installed: {Code}", connectorId, ex.ErrorCode);
                results.Add(new("connector", connectorId, connectorId, ex.HttpStatusCode == 404 ? "unavailable" : "failed", ex.Message));
            }
        }

        var existingNames = await _db.WorkflowDefinitions.Where(w => w.WorkspaceId == workspaceId).Select(w => w.Name).ToListAsync(ct);
        foreach (var slug in pack.Templates)
        {
            try
            {
                var template = await FindTemplateAsync(slug, ct);
                // Installing the pack again shouldn't make a second copy of every workflow.
                if (existingNames.Contains(template.Name, StringComparer.OrdinalIgnoreCase))
                {
                    results.Add(new("template", slug, template.Name, "already_installed",
                        $"This workspace already has a workflow called '{template.Name}'. To add another copy, install the template on its own and give it a new name."));
                    continue;
                }
                var workflowId = await InstallTemplateAsync(workspaceId, slug, null, userId, ct);
                existingNames.Add(template.Name);
                results.Add(new("template", slug, template.Name, "installed", $"Added the workflow '{template.Name}'.", workflowId));
            }
            catch (AppException ex)
            {
                _log.LogWarning("LibraryService.InstallPackAsync template {Template} not installed: {Code}", slug, ex.ErrorCode);
                results.Add(new("template", slug, slug, ex.HttpStatusCode == 404 ? "unavailable" : "failed", ex.Message));
            }
        }

        // Forms have nothing to install: they're offered on every human gate's "Use a Library form".
        var forms = await VisibleForms(workspaceId).Where(f => pack.Forms.Contains(f.Slug)).ToListAsync(ct);
        foreach (var slug in pack.Forms)
        {
            var form = forms.FirstOrDefault(f => f.Slug == slug);
            results.Add(form is null
                ? new("form", slug, slug, "unavailable", $"The form '{slug}' isn't in the Library any more. The rest of the pack works without it.")
                : new("form", slug, form.Name, "already_installed", $"{form.Name} is ready to use: open a human gate and choose \"Use a Library form\"."));
        }

        var installed = results.Count(r => r.Status == "installed");
        var already = results.Count(r => r.Status == "already_installed");
        var failed = results.Count(r => r.Status is "failed" or "unavailable");
        _log.LogInformation("LibraryService.InstallPackAsync exit pack={Pack} installed={Installed} already={Already} failed={Failed}",
            pack.Slug, installed, already, failed);
        return new LibraryPackInstallResult(pack.Name, installed, already, failed, results);
    }

    private sealed record PackContext(
        IReadOnlyList<LibraryConnectorItem> Connectors,
        IReadOnlyList<WorkflowTemplate> Templates,
        IReadOnlyList<FormDefinition> Forms,
        HashSet<string> WorkflowNames);

    private async Task<PackContext> PackContextAsync(Guid workspaceId, CancellationToken ct) => new(
        await ListConnectorsAsync(workspaceId, null, null, ct),
        await OfferedTemplates().ToListAsync(ct),
        await VisibleForms(workspaceId).ToListAsync(ct),
        (await _db.WorkflowDefinitions.Where(w => w.WorkspaceId == workspaceId).Select(w => w.Name).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase));

    private static LibraryPackItem ToPack(PackDefinition p, PackContext c)
    {
        var entries = new List<LibraryPackEntry>();
        foreach (var id in p.Connectors)
        {
            var parts = id.Split('/');
            var connector = c.Connectors.FirstOrDefault(x => parts.Length == 2 ? x.Publisher == parts[0] && x.Id == parts[1] : x.Id == id);
            entries.Add(new("connector", id, connector?.Name ?? id, connector is not null, connector?.Installed == true));
        }
        foreach (var slug in p.Templates)
        {
            var template = c.Templates.FirstOrDefault(t => t.Slug == slug);
            entries.Add(new("template", slug, template?.Name ?? slug, template is not null, template is not null && c.WorkflowNames.Contains(template.Name)));
        }
        foreach (var slug in p.Forms)
        {
            var form = c.Forms.FirstOrDefault(f => f.Slug == slug);
            entries.Add(new("form", slug, form?.Name ?? slug, form is not null, form is not null));
        }
        return new LibraryPackItem(p.Id, p.Slug, p.Name, p.Description, p.Category, p.Version, p.PublisherId, p.IsOfficial,
            entries, p.Tags, p.DeprecatedAt, p.DeprecationNote);
    }

    private async Task<PackDefinition> FindPackAsync(string slugOrId, CancellationToken ct)
    {
        var key = slugOrId.Trim();
        var byId = Guid.TryParse(key, out var id);
        return await _db.PackDefinitions.AsNoTracking().FirstOrDefaultAsync(p => byId ? p.Id == id : p.Slug == key, ct)
               ?? throw LibraryException.PackNotFound(slugOrId);
    }

    private const int ConnectorDefinitionNoticeDays = Flowamaz.Core.Entities.Connector.ConnectorDefinition.DeprecationNoticeDays;

    private static string[] Words(string? query) =>
        string.IsNullOrWhiteSpace(query) ? [] : query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
