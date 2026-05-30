using System.Text.Json;
using Flowamaz.Core.Entities.Audit;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Fire-and-forget audit recording (same pattern as <see cref="AiTokenMeteringService"/>).
/// <see cref="RecordAsync"/> resolves IP/user-agent from the ambient request synchronously (the
/// HttpContext may be gone by the time the background task runs), then writes the row on a
/// background <c>Task.Run</c> with a fresh DI scope. Errors are caught and logged — they never
/// propagate, never throw, and never block the request that triggered the audit event.
/// </summary>
public sealed class AuditService : IAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRequestContextAccessor _requestContext;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        IServiceScopeFactory scopeFactory,
        IRequestContextAccessor requestContext,
        ILogger<AuditService> logger)
    {
        _scopeFactory = scopeFactory;
        _requestContext = requestContext;
        _logger = logger;
    }

    public void RecordAsync(AuditEventRequest request, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "AuditService.RecordAsync enter event={EventType} resource={ResourceType} action={Action} workspace={WorkspaceId} org={OrgId}",
            request.EventType, request.ResourceType, request.Action, request.WorkspaceId, request.OrgId);

        // Resolve IP / user-agent NOW — the HttpContext may not exist on the background task.
        var ip = request.IpAddress ?? _requestContext.GetIpAddress();
        var userAgent = request.UserAgent ?? _requestContext.GetUserAgent();

        var record = new AuditEvent
        {
            OrgId = request.OrgId,
            WorkspaceId = request.WorkspaceId,
            ActorUserId = request.ActorUserId,
            ActorType = request.ActorType,
            ActorLabel = request.ActorLabel,
            EventType = request.EventType,
            ResourceType = request.ResourceType,
            ResourceId = request.ResourceId,
            ResourceLabel = request.ResourceLabel,
            Action = request.Action,
            Metadata = request.Metadata is null ? null : JsonSerializer.Serialize(request.Metadata, JsonOptions),
            IpAddress = ip,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow,
        };

        // Fire-and-forget — do NOT await. The request path must never wait for or fail on auditing.
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();

                // Resolve org_id from the workspace when the caller did not supply it.
                if (record.OrgId == Guid.Empty && record.WorkspaceId is not null)
                {
                    record.OrgId = await db.Workspaces
                        .AsNoTracking()
                        .Where(w => w.Id == record.WorkspaceId.Value)
                        .Select(w => w.OrgId)
                        .FirstOrDefaultAsync();
                }

                await db.AuditEvents.AddAsync(record);
                await db.SaveChangesAsync();
                _logger.LogDebug("AuditService.RecordAsync exit id={AuditId} event={EventType}", record.Id, record.EventType);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _logger.LogError(ex,
                    "AuditService.RecordAsync error — audit record dropped (event={EventType} workspace={WorkspaceId})",
                    record.EventType, record.WorkspaceId);
            }
        });
    }
}
