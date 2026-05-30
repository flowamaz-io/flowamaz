using System.Globalization;
using System.Text;
using Flowamaz.Api.Authorization;
using Flowamaz.Core.Entities.Audit;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Immutable audit-trail read API (prompt 07-02). Workspace feed is Admin-gated; org-wide feed and
/// CSV export are org-owner-gated. Every query is org-scoped (workspace isolation) — a caller can
/// only ever see audit events from their own organisation.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class AuditController : ControllerBase
{
    private const int MaxQueryRangeDays = 90;
    private const int MaxExportRangeDays = 365;

    private readonly IAuditEventRepository _audit;
    private readonly IWorkspaceRepository _workspaces;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<AuditController> _logger;

    public AuditController(
        IAuditEventRepository audit,
        IWorkspaceRepository workspaces,
        ICurrentUserService currentUser,
        ILogger<AuditController> logger)
    {
        _audit = audit;
        _workspaces = workspaces;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>Workspace audit feed, newest first. Max range 90 days, max page size 100.</summary>
    [HttpGet("workspaces/{workspaceId:guid}/audit")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<ActionResult<AuditPageResponse>> ListForWorkspace(
        Guid workspaceId, [FromQuery] AuditQueryParams query, CancellationToken ct)
    {
        _logger.LogInformation("AuditController.ListForWorkspace enter workspace={WorkspaceId}", workspaceId);

        var orgId = RequireOrg();
        var (from, to) = ResolveRange(query, MaxQueryRangeDays);

        var filter = new AuditQueryFilter
        {
            OrgId = orgId,
            WorkspaceId = workspaceId,
            From = from,
            To = to,
            EventType = query.EventType,
            ActorUserId = query.ActorId,
            ResourceType = query.ResourceType,
            Page = query.Page,
            PageSize = query.PageSize,
        };

        var (items, total) = await _audit.QueryAsync(filter, ct);
        _logger.LogInformation("AuditController.ListForWorkspace exit workspace={WorkspaceId} total={Total}", workspaceId, total);
        return Ok(BuildPage(items, total, filter));
    }

    /// <summary>Org-wide audit feed (all workspaces). Org owner only. Max range 90 days.</summary>
    [HttpGet("organisations/{id:guid}/audit")]
    [RequireOrgOwner]
    public async Task<ActionResult<AuditPageResponse>> ListForOrg(
        Guid id, [FromQuery] AuditQueryParams query, CancellationToken ct)
    {
        _logger.LogInformation("AuditController.ListForOrg enter org={OrgId}", id);

        EnsureOwnOrg(id);
        var (from, to) = ResolveRange(query, MaxQueryRangeDays);

        var filter = new AuditQueryFilter
        {
            OrgId = id,
            WorkspaceId = null,
            From = from,
            To = to,
            EventType = query.EventType,
            ActorUserId = query.ActorId,
            ResourceType = query.ResourceType,
            Page = query.Page,
            PageSize = query.PageSize,
        };

        var (items, total) = await _audit.QueryAsync(filter, ct);
        _logger.LogInformation("AuditController.ListForOrg exit org={OrgId} total={Total}", id, total);
        return Ok(BuildPage(items, total, filter));
    }

    /// <summary>CSV export of a workspace's audit events. Org owner only. Max range 365 days. Streamed.</summary>
    [HttpGet("workspaces/{workspaceId:guid}/audit/export")]
    [RequireOrgOwner]
    public async Task ExportForWorkspace(
        Guid workspaceId, [FromQuery] AuditQueryParams query, CancellationToken ct)
    {
        _logger.LogInformation("AuditController.ExportForWorkspace enter workspace={WorkspaceId}", workspaceId);

        var orgId = RequireOrg();
        // Workspace isolation: the workspace must belong to the caller's org.
        var workspace = await _workspaces.GetByIdAsync(workspaceId, ct);
        if (workspace is null || workspace.OrgId != orgId)
            throw new InsufficientRoleException(WorkspaceRole.Admin, null);

        var (from, to) = ResolveRange(query, MaxExportRangeDays);
        var filter = new AuditQueryFilter
        {
            OrgId = orgId,
            WorkspaceId = workspaceId,
            From = from,
            To = to,
            EventType = query.EventType,
            ActorUserId = query.ActorId,
            ResourceType = query.ResourceType,
        };

        Response.ContentType = "text/csv";
        Response.Headers.ContentDisposition =
            $"attachment; filename=\"audit-{workspaceId}-{from:yyyyMMdd}-{to:yyyyMMdd}.csv\"";

        await using var writer = new StreamWriter(Response.Body, new UTF8Encoding(false));
        await writer.WriteLineAsync(
            "timestamp,event_type,actor_type,actor,action,resource_type,resource,ip_address");

        await foreach (var e in _audit.StreamAsync(filter, ct))
        {
            await writer.WriteLineAsync(string.Join(',',
                Csv(e.CreatedAt.ToString("o", CultureInfo.InvariantCulture)),
                Csv(e.EventType),
                Csv(e.ActorType),
                Csv(e.ActorLabel),
                Csv(e.Action),
                Csv(e.ResourceType),
                Csv(e.ResourceLabel),
                Csv(e.IpAddress)));
        }

        await writer.FlushAsync(ct);
        _logger.LogInformation("AuditController.ExportForWorkspace exit workspace={WorkspaceId}", workspaceId);
    }

    private AuditPageResponse BuildPage(IReadOnlyList<AuditEvent> items, int total, AuditQueryFilter filter)
    {
        var size = Math.Clamp(filter.PageSize <= 0 ? 20 : filter.PageSize, 1, 100);
        var page = filter.Page <= 0 ? 1 : filter.Page;
        var totalPages = (int)Math.Ceiling(total / (double)size);
        var rows = items.Select(ToRow).ToList();
        return new AuditPageResponse(rows, new AuditPagination(page, size, total, totalPages));
    }

    private static AuditRow ToRow(AuditEvent e) => new(
        e.Id,
        e.CreatedAt,
        e.ActorType,
        e.ActorLabel,
        e.ActorUserId,
        e.EventType,
        e.Action,
        e.ResourceType,
        e.ResourceId,
        e.ResourceLabel,
        e.IpAddress,
        e.UserAgent,
        Summarise(e),
        e.Metadata);

    /// <summary>Human-readable one-line summary, e.g. "Jagan created workflow 'Fund Pipeline'".</summary>
    private static string Summarise(AuditEvent e)
    {
        var actor = string.IsNullOrWhiteSpace(e.ActorLabel)
            ? e.ActorType switch { "system" => "System", "webhook" => "Webhook", "api_key" => "An API key", _ => "A user" }
            : e.ActorLabel;
        var resource = string.IsNullOrWhiteSpace(e.ResourceLabel)
            ? e.ResourceType
            : $"{e.ResourceType} '{e.ResourceLabel}'";
        return $"{actor} {e.Action} {resource}";
    }

    private (DateTime From, DateTime To) ResolveRange(AuditQueryParams query, int maxDays)
    {
        var to = query.To ?? DateTime.UtcNow;
        var from = query.From ?? to.AddDays(-7);
        if (from > to) throw AuditException.InvalidRange();
        if ((to - from).TotalDays > maxDays) throw AuditException.RangeTooWide(maxDays);
        return (from, to);
    }

    private Guid RequireOrg() =>
        _currentUser.OrgId ?? throw new AuditException(
            "AUTH_REQUIRED",
            "Sign in to your organisation to view the audit log. Your session has no organisation context.",
            httpStatusCode: 401);

    private void EnsureOwnOrg(Guid requestedOrgId)
    {
        var orgId = RequireOrg();
        if (orgId != requestedOrgId)
            throw new InsufficientRoleException(WorkspaceRole.Admin, null);
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}

public sealed record AuditQueryParams
{
    [FromQuery(Name = "from")] public DateTime? From { get; init; }
    [FromQuery(Name = "to")] public DateTime? To { get; init; }
    [FromQuery(Name = "event_type")] public string? EventType { get; init; }
    [FromQuery(Name = "actor_id")] public Guid? ActorId { get; init; }
    [FromQuery(Name = "resource_type")] public string? ResourceType { get; init; }
    [FromQuery(Name = "page")] public int Page { get; init; } = 1;
    [FromQuery(Name = "page_size")] public int PageSize { get; init; } = 20;
}

public sealed record AuditRow(
    Guid Id,
    DateTime Timestamp,
    string ActorType,
    string? Actor,
    Guid? ActorUserId,
    string EventType,
    string Action,
    string ResourceType,
    Guid? ResourceId,
    string? Resource,
    string? IpAddress,
    string? UserAgent,
    string Summary,
    string? Metadata);

public sealed record AuditPagination(int Page, int PageSize, int Total, int TotalPages);

public sealed record AuditPageResponse(IReadOnlyList<AuditRow> Data, AuditPagination Pagination);
