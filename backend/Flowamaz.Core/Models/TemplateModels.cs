namespace Flowamaz.Core.Models;

/// <summary>A page of results with the total count, returned by gallery listing.</summary>
public sealed record TemplatePage(IReadOnlyList<TemplateListItem> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => Total == 0 ? 0 : (int)System.Math.Ceiling(Total / (double)PageSize);
}

/// <summary>Summary row for the template gallery (no YAML body).</summary>
public sealed record TemplateListItem(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    string Category,
    bool IsOfficial,
    string? PreviewImageUrl,
    int InstallCount,
    decimal AverageRating,
    string[] Tags,
    string Version,
    DateTime CreatedAt);

/// <summary>Full template detail including the YAML body and a derived node-type summary.</summary>
public sealed record TemplateDetail(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    string Category,
    bool IsOfficial,
    string? PreviewImageUrl,
    int InstallCount,
    decimal AverageRating,
    string[] Tags,
    string Version,
    string YamlContent,
    IReadOnlyList<TemplateNodeSummary> NodeSummary,
    DateTime CreatedAt);

/// <summary>"This template uses: 2× human-gate, 3× action…" — a count per node type.</summary>
public sealed record TemplateNodeSummary(string NodeType, int Count);

/// <summary>Details supplied when publishing a workflow as a community template.</summary>
public sealed record PublishTemplateDetails(
    string Name,
    string Description,
    string Category,
    string[] Tags,
    string? PreviewImageUrl);

/// <summary>Outcome of publishing a community template.</summary>
public sealed record PublishTemplateResult(Guid TemplateId, string Slug, string ReviewStatus);
