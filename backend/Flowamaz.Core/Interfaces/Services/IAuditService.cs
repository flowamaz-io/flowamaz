using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Records immutable audit events. <see cref="RecordAsync"/> is fire-and-forget: it returns
/// immediately, performs the DB write on a background task with a fresh DI scope, and never throws
/// to the caller. Wiring an audit event must never block or break the originating operation.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Queue an audit event for persistence. Resolves IP/user-agent from the ambient request when
    /// the request does not supply them. Returns immediately; errors are logged, never propagated.
    /// </summary>
    void RecordAsync(AuditEventRequest request, CancellationToken ct = default);
}
