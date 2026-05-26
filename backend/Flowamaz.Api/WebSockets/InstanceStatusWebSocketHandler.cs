using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Api.WebSockets;

/// <summary>
/// Pushes live instance status over a WebSocket. Auth is via a <c>?token=</c> query param (the
/// standard WS pattern — browsers can't set Authorization headers), validated against the JWT and
/// the caller's workspace memberships before the socket is accepted. After sending a snapshot it
/// polls the instance (fresh scope per tick) and pushes a frame on each status/node change until
/// the run ends or the client disconnects. Capped per workspace (default 100).
/// </summary>
public sealed class InstanceStatusWebSocketHandler
{
    private static readonly ConcurrentDictionary<Guid, int> ConnectionsPerWorkspace = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly IJwtService _jwt;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InstanceStatusWebSocketHandler> _logger;
    private readonly int _maxPerWorkspace;

    public InstanceStatusWebSocketHandler(
        IJwtService jwt,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<InstanceStatusWebSocketHandler> logger)
    {
        _jwt = jwt;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _maxPerWorkspace = Math.Max(1, configuration.GetValue<int?>("WebSocket:MaxConnectionsPerWorkspace") ?? 100);
    }

    public async Task HandleAsync(HttpContext context, Guid workspaceId, Guid instanceId)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var token = context.Request.Query["token"].ToString();
        var principal = string.IsNullOrEmpty(token) ? null : _jwt.ValidateAccessToken(token);
        if (principal is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var memberships = _jwt.ExtractWorkspaceMemberships(principal);
        if (!memberships.Any(m => m.WorkspaceId == workspaceId))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (ConnectionsPerWorkspace.AddOrUpdate(workspaceId, 1, (_, n) => n + 1) > _maxPerWorkspace)
        {
            ConnectionsPerWorkspace.AddOrUpdate(workspaceId, 0, (_, n) => n - 1);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var ct = context.RequestAborted;
        _logger.LogInformation("InstanceStatusWebSocketHandler connected workspace={WorkspaceId} instance={InstanceId}", workspaceId, instanceId);

        try
        {
            string? lastStatus = null;
            string? lastNode = null;

            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var snapshot = await ReadInstanceAsync(workspaceId, instanceId, ct);
                if (snapshot is null)
                {
                    await SendAsync(socket, new StatusFrame("InstanceNotFound", "Unknown", null, DateTime.UtcNow), ct);
                    break;
                }

                if (snapshot.Status != lastStatus || snapshot.CurrentNodeId != lastNode)
                {
                    var eventType = lastStatus is null ? "Snapshot" : "StatusChanged";
                    await SendAsync(socket, new StatusFrame(eventType, snapshot.Status, snapshot.CurrentNodeId, DateTime.UtcNow), ct);
                    lastStatus = snapshot.Status;
                    lastNode = snapshot.CurrentNodeId;
                }

                if (snapshot.IsTerminal) break;
                await Task.Delay(PollInterval, ct);
            }

            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "complete", CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal.
        }
        catch (WebSocketException ex)
        {
            _logger.LogDebug(ex, "InstanceStatusWebSocketHandler socket closed instance={InstanceId}", instanceId);
        }
        finally
        {
            ConnectionsPerWorkspace.AddOrUpdate(workspaceId, 0, (_, n) => Math.Max(0, n - 1));
            _logger.LogInformation("InstanceStatusWebSocketHandler disconnected workspace={WorkspaceId} instance={InstanceId}", workspaceId, instanceId);
        }
    }

    private async Task<InstanceSnapshot?> ReadInstanceAsync(Guid workspaceId, Guid instanceId, CancellationToken ct)
    {
        // Fresh scope per poll so we read committed DB state, not a cached tracked entity.
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceRepository>();
        var instance = await repo.GetByIdForWorkspaceAsync(instanceId, workspaceId, ct);
        if (instance is null) return null;

        var status = instance.Status.ToString();
        var terminal = instance.Status is Core.Enums.InstanceStatus.Completed
            or Core.Enums.InstanceStatus.Failed
            or Core.Enums.InstanceStatus.Cancelled;
        return new InstanceSnapshot(status, instance.CurrentNodeId, terminal);
    }

    private static async Task SendAsync(WebSocket socket, StatusFrame frame, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(frame, JsonOptions);
        await socket.SendAsync(json, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private sealed record InstanceSnapshot(string Status, string? CurrentNodeId, bool IsTerminal);

    private sealed record StatusFrame(string EventType, string Status, string? NodeId, DateTime Timestamp);
}
