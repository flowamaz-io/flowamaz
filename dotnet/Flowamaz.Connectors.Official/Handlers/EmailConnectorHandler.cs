using System.Net;
using System.Text.Json;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// Sends a transactional email through the platform email service (Resend, noreply@ sender).
/// Input: { to, subject, body, is_html? }. Plain-text bodies are HTML-encoded before wrapping so
/// workflow data cannot inject markup into mail sent from the platform domain.
/// Mail goes out from the platform's sender, so each workspace is capped per hour and per day
/// (Connectors:Email:MaxPerHour / MaxPerDay, default 100 / 500) to stop a workflow being used
/// to spam from Flowamaz's domain. Workspaces that need more should use their own Microsoft 365
/// or SMTP account.
/// </summary>
public sealed class EmailSendHandler : IConnectorOperationHandler
{
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailSendHandler> _logger;
    private readonly IRateLimitService? _rateLimit;
    private readonly ConnectorCallContext? _context;
    private readonly int _maxPerHour;
    private readonly int _maxPerDay;

    public EmailSendHandler(
        IEmailService emailService,
        ILogger<EmailSendHandler> logger,
        IRateLimitService? rateLimit = null,
        ConnectorCallContext? context = null,
        IConfiguration? configuration = null)
    {
        _emailService = emailService;
        _logger = logger;
        _rateLimit = rateLimit;
        _context = context;
        _maxPerHour = int.TryParse(configuration?["Connectors:Email:MaxPerHour"], out var h) && h > 0 ? h : 100;
        _maxPerDay = int.TryParse(configuration?["Connectors:Email:MaxPerDay"], out var d) && d > 0 ? d : 500;
    }

    public string ConnectorId => "email-smtp";
    public string OperationId => "send-email";

    public async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        _logger.LogInformation("[Email:send-email] ExecuteAsync entry");
        const string op = "email-smtp/send-email";
        var root = input.RootElement;
        var to = ConnectorInput.Required(root, "to", op);
        var subject = ConnectorInput.Required(root, "subject", op);
        var body = ConnectorInput.Required(root, "body", op);
        var isHtml = ConnectorInput.Bool(root, "is_html");

        if (!System.Net.Mail.MailAddress.TryCreate(to, out _))
            throw new ConnectorOperationException($"{op}: '{to}' is not a valid email address. Check the 'to' input.");
        if (subject.Contains('\r') || subject.Contains('\n'))
            throw new ConnectorOperationException($"{op}: the subject must be a single line.");

        await EnforceSendLimitsAsync(op, ct);

        var htmlBody = isHtml ? body : $"<pre style=\"font-family:inherit;white-space:pre-wrap\">{WebUtility.HtmlEncode(body)}</pre>";
        var textBody = isHtml ? null : body;

        var sent = await _emailService.SendAsync(to, subject, htmlBody, textBody, ct);
        if (!sent)
            throw new ConnectorOperationException(
                $"{op}: the email provider did not accept the message. It will be retried per the node's retry policy.", retryable: true);

        _logger.LogInformation("[Email:send-email] ExecuteAsync exit");
        return ConnectorHttp.ToDocument(new { sent = true });
    }

    private async Task EnforceSendLimitsAsync(string op, CancellationToken ct)
    {
        if (_rateLimit is null || _context?.WorkspaceId is not { } workspaceId) return;

        if (!await _rateLimit.CheckAndIncrementAsync($"email-connector:day:{workspaceId:N}", _maxPerDay, 86_400, ct))
            throw new ConnectorOperationException(
                $"{op}: this workspace reached its daily limit of {_maxPerDay} platform emails. " +
                "Use the Microsoft 365 connector to send from your own mailbox, or contact support@flowamaz.io to raise the limit.");

        if (!await _rateLimit.CheckAndIncrementAsync($"email-connector:hour:{workspaceId:N}", _maxPerHour, 3_600, ct))
            throw new ConnectorOperationException(
                $"{op}: this workspace reached its hourly limit of {_maxPerHour} platform emails. The step will retry later.",
                retryable: true);
    }
}
