namespace Flowamaz.Core.Models;

/// <summary>
/// The data captured for a single audit event. Built by the service layer at the point an action
/// completes and handed to <c>IAuditService.RecordAsync</c>. IP/user-agent are resolved by the
/// service from the ambient HttpContext when not supplied.
/// </summary>
public sealed class AuditEventRequest
{
    public Guid OrgId { get; init; }
    public Guid? WorkspaceId { get; init; }
    public Guid? ActorUserId { get; init; }

    /// <summary>user | api_key | system | webhook.</summary>
    public string ActorType { get; init; } = "system";
    public string? ActorLabel { get; init; }

    public string EventType { get; init; } = string.Empty;
    public string ResourceType { get; init; } = string.Empty;
    public Guid? ResourceId { get; init; }
    public string? ResourceLabel { get; init; }
    public string Action { get; init; } = string.Empty;

    /// <summary>Structured before/after context. Serialised to jsonb. MUST NOT contain secrets.</summary>
    public object? Metadata { get; init; }

    /// <summary>Optional explicit IP; when null the service reads it from the current request.</summary>
    public string? IpAddress { get; init; }

    /// <summary>Optional explicit user agent; when null the service reads it from the current request.</summary>
    public string? UserAgent { get; init; }
}
