namespace Flowamaz.Core.Entities.Audit;

/// <summary>
/// An immutable, append-only audit record of a significant action (workflow created, instance
/// triggered, gate decided, member invited, …). For compliance (SOC 2 / ISO 27001 / GDPR).
/// Not a <see cref="BaseEntity"/>: audit events are NEVER updated or soft-deleted — the only
/// permitted DELETE is the server-side retention job (<c>AuditRetentionJob</c>). There is no
/// UpdatedAt and no soft-delete column.
/// </summary>
public class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrgId { get; set; }

    /// <summary>Null for org-level events that are not scoped to a single workspace.</summary>
    public Guid? WorkspaceId { get; set; }

    /// <summary>The acting org user; null for system/webhook events with no human actor.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>user | api_key | system | webhook.</summary>
    public string ActorType { get; set; } = "system";

    /// <summary>Human-readable actor label (user display name or API key name).</summary>
    public string? ActorLabel { get; set; }

    /// <summary>e.g. workflow.created, workflow.published, instance.started, gate.approved.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>workflow | instance | member | connector | api_key | gate | webhook | sso | billing.</summary>
    public string ResourceType { get; set; } = string.Empty;

    public Guid? ResourceId { get; set; }

    /// <summary>Human-readable name of the resource (workflow name, member email, …).</summary>
    public string? ResourceLabel { get; set; }

    /// <summary>created | updated | deleted | published | triggered | approved | rejected | invited | revoked.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Before/after state and other structured context, stored as jsonb. Never contains secrets.</summary>
    public string? Metadata { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
