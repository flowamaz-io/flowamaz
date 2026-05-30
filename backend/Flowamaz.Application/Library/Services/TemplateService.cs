using System.Text.RegularExpressions;
using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Entities.Library;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Library;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Flowamaz.Application.Library.Services;

/// <summary>
/// Template gallery service. Browse is public; install creates a new WorkflowDefinition from the
/// template's YAML through the normal <see cref="WorkflowService"/> create path (so versioning, Git
/// init and edition gating all run identically to a hand-authored workflow) and atomically bumps
/// install_count. Publish turns a Published workflow into a pending-review community template.
/// </summary>
public sealed partial class TemplateService : ITemplateService
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly IWorkflowTemplateRepository _templates;
    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly WorkflowService _workflows;
    private readonly IWorkflowValidator _validator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TemplateService> _logger;
    private readonly IAuditService? _audit;

    public TemplateService(
        IWorkflowTemplateRepository templates,
        IWorkflowDefinitionRepository definitions,
        WorkflowService workflows,
        IWorkflowValidator validator,
        IUnitOfWork unitOfWork,
        ILogger<TemplateService> logger,
        IAuditService? audit = null)
    {
        _templates = templates;
        _definitions = definitions;
        _workflows = workflows;
        _validator = validator;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _audit = audit;
    }

    public async Task<TemplatePage> ListTemplatesAsync(
        string? category, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        _logger.LogInformation("TemplateService.ListTemplatesAsync enter category={Category} search={Search} page={Page}",
            category, search, page);

        var all = await _templates.ListAsync(category, search, ct);

        var clampedSize = Math.Clamp(pageSize <= 0 ? 12 : pageSize, 1, 100);
        var clampedPage = page <= 0 ? 1 : page;
        var items = all
            .Skip((clampedPage - 1) * clampedSize)
            .Take(clampedSize)
            .Select(ToListItem)
            .ToList();

        _logger.LogInformation("TemplateService.ListTemplatesAsync exit total={Total} returned={Returned}", all.Count, items.Count);
        return new TemplatePage(items, clampedPage, clampedSize, all.Count);
    }

    public async Task<TemplateDetail?> GetTemplateAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("TemplateService.GetTemplateAsync enter template={TemplateId}", id);

        var template = await _templates.GetByIdAsync(id, ct);
        if (template is null)
        {
            _logger.LogInformation("TemplateService.GetTemplateAsync exit template={TemplateId} found=false", id);
            return null;
        }

        var nodeSummary = BuildNodeSummary(template.YamlContent);

        _logger.LogInformation("TemplateService.GetTemplateAsync exit template={TemplateId} found=true", id);
        return new TemplateDetail(
            template.Id, template.Name, template.Slug, template.Description, template.Category,
            template.IsOfficial, template.PreviewImageUrl, template.InstallCount, template.AverageRating,
            template.Tags, template.Version, template.YamlContent, nodeSummary, template.CreatedAt);
    }

    public async Task<Guid> InstallTemplateAsync(
        Guid templateId, Guid workspaceId, Guid userId, string name, CancellationToken ct = default)
    {
        _logger.LogInformation("TemplateService.InstallTemplateAsync enter template={TemplateId} workspace={WorkspaceId}",
            templateId, workspaceId);

        var template = await _templates.GetByIdAsync(templateId, ct)
            ?? throw TemplateException.NotFound(templateId);

        var workflowName = string.IsNullOrWhiteSpace(name) ? template.Name : name.Trim();

        // Reuse the normal workflow create path so the installed workflow is identical to one
        // authored by hand: YAML is parsed/validated, Git-initialised, edition-gated and versioned.
        var slug = await BuildUniqueSlugAsync(workspaceId, workflowName, ct);
        var created = await _workflows.CreateAsync(
            workspaceId,
            userId,
            new CreateWorkflowDefinitionRequest(
                workflowName,
                slug,
                template.YamlContent,
                $"Installed from template: {template.Name}",
                WorkflowCreatedByMethod.Canvas),
            ct);

        // Atomic, DB-level increment — no read-modify-write race under concurrent installs.
        await _templates.IncrementInstallCountAsync(template.Id, ct);

        _audit?.RecordAsync(new AuditEventRequest
        {
            WorkspaceId = workspaceId,
            ActorUserId = userId,
            ActorType = "user",
            EventType = "template.installed",
            ResourceType = "workflow_template",
            ResourceId = template.Id,
            ResourceLabel = template.Name,
            Action = "installed",
            Metadata = new { workflowId = created.Id },
        }, ct);

        _logger.LogInformation("TemplateService.InstallTemplateAsync exit template={TemplateId} workflow={WorkflowId}",
            templateId, created.Id);
        return created.Id;
    }

    public async Task<PublishTemplateResult> PublishTemplateAsync(
        Guid workflowId, Guid workspaceId, Guid userId, Guid orgId, PublishTemplateDetails details, CancellationToken ct = default)
    {
        _logger.LogInformation("TemplateService.PublishTemplateAsync enter workflow={WorkflowId} workspace={WorkspaceId}",
            workflowId, workspaceId);

        if (string.IsNullOrWhiteSpace(details.Name))
            throw TemplateException.Validation("A template name is required to publish.");
        if (string.IsNullOrWhiteSpace(details.Category))
            throw TemplateException.Validation("A category is required to publish a template.");

        var definition = await _definitions.GetByIdForWorkspaceAsync(workflowId, workspaceId, ct)
            ?? throw TemplateException.WorkflowNotFound(workflowId);

        if (definition.Status != WorkflowStatus.Published)
            throw TemplateException.WorkflowNotPublished();

        // Validate via the six-layer validator (never string manipulation) — refuse a broken template.
        var validation = await _validator.ValidateAsync(definition.YamlContent, workspaceId, ct);
        if (!validation.IsValid)
            throw TemplateException.InvalidYaml(validation.Errors[0].Message);

        var slug = await BuildUniqueTemplateSlugAsync(details.Name, ct);

        var template = new WorkflowTemplate
        {
            Name = details.Name.Trim(),
            Slug = slug,
            Description = details.Description?.Trim() ?? string.Empty,
            Category = details.Category.Trim(),
            Tags = details.Tags ?? [],
            PreviewImageUrl = details.PreviewImageUrl,
            YamlContent = definition.YamlContent,
            IsOfficial = false,
            OrgId = orgId,
            Version = "1.0.0",
            IsActive = true,
            ReviewStatus = "pending",
            CreatedBy = userId,
        };
        await _templates.AddAsync(template, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _audit?.RecordAsync(new AuditEventRequest
        {
            OrgId = orgId,
            WorkspaceId = workspaceId,
            ActorUserId = userId,
            ActorType = "user",
            EventType = "template.published",
            ResourceType = "workflow_template",
            ResourceId = template.Id,
            ResourceLabel = template.Name,
            Action = "published",
        }, ct);

        _logger.LogInformation("TemplateService.PublishTemplateAsync exit template={TemplateId} status={Status}",
            template.Id, template.ReviewStatus);
        return new PublishTemplateResult(template.Id, template.Slug, template.ReviewStatus);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static TemplateListItem ToListItem(WorkflowTemplate t) => new(
        t.Id, t.Name, t.Slug, t.Description, t.Category, t.IsOfficial, t.PreviewImageUrl,
        t.InstallCount, t.AverageRating, t.Tags, t.Version, t.CreatedAt);

    private IReadOnlyList<TemplateNodeSummary> BuildNodeSummary(string yaml)
    {
        try
        {
            // flowamaz/v1: spec.nodes[].type. Deserialise via YamlDotNet (never string manipulation).
            var doc = YamlDeserializer.Deserialize<TemplateDoc>(yaml);
            var nodes = doc?.Spec?.Nodes ?? [];
            return nodes
                .Where(n => !string.IsNullOrWhiteSpace(n.Type))
                .GroupBy(n => n.Type!)
                .Select(g => new TemplateNodeSummary(g.Key, g.Count()))
                .OrderByDescending(s => s.Count)
                .ToList();
        }
        catch (Exception ex)
        {
            // A seeded/published template's YAML always parses; tolerate a bad row rather than 500.
            _logger.LogWarning(ex, "TemplateService.BuildNodeSummary could not parse template YAML");
            return [];
        }
    }

    private sealed class TemplateDoc
    {
        public TemplateSpec? Spec { get; set; }
    }

    private sealed class TemplateSpec
    {
        public List<TemplateNode>? Nodes { get; set; }
    }

    private sealed class TemplateNode
    {
        public string? Type { get; set; }
    }

    private async Task<string> BuildUniqueSlugAsync(Guid workspaceId, string name, CancellationToken ct)
    {
        var baseSlug = Slugify(name);
        var slug = baseSlug;
        var suffix = 1;
        while (await _definitions.SlugExistsInWorkspaceAsync(workspaceId, slug, ct))
        {
            slug = $"{baseSlug}-{suffix++}";
        }
        return slug;
    }

    private async Task<string> BuildUniqueTemplateSlugAsync(string name, CancellationToken ct)
    {
        var baseSlug = Slugify(name);
        var slug = baseSlug;
        var suffix = 1;
        while (await _templates.SlugExistsAsync(slug, ct))
        {
            slug = $"{baseSlug}-{suffix++}";
        }
        return slug;
    }

    private static string Slugify(string value)
    {
        var lower = value.Trim().ToLowerInvariant();
        var slug = NonSlugChars().Replace(lower, "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "workflow" : slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();
}
