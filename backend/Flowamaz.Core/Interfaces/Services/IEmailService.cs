namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Transactional email via Resend (FUNCTIONAL.md §1.3). Logs every send attempt with a
/// correlation ID. Never throws — failures are logged and absorbed so they don't propagate
/// into business logic. Caller decides whether to retry or compensate.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Send a transactional email from noreply@flowamaz.io to <paramref name="to"/>.
    /// Returns true on 2xx from Resend, false on any failure (incl. missing API key).
    /// </summary>
    Task<bool> SendAsync(
        string to,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default);
}
