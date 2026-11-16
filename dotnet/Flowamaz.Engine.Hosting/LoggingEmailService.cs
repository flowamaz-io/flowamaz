using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// The default <see cref="IEmailService"/> of an engine host: it logs that an email would be sent (the
/// subject only: no address, and never the body, which for a gate carries signed approval links) and
/// reports it as not sent. Register your own <see cref="IEmailService"/> (SMTP, a mail API) to deliver gate emails.
/// </summary>
public sealed class LoggingEmailService : IEmailService
{
    private readonly ILogger<LoggingEmailService> _logger;

    public LoggingEmailService(ILogger<LoggingEmailService> logger) => _logger = logger;

    public Task<bool> SendAsync(string to, string subject, string htmlBody, string? textBody = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "LoggingEmailService: no email sender is registered; not sending \"{Subject}\". Register an IEmailService to deliver it.",
            subject);
        return Task.FromResult(false);
    }
}
