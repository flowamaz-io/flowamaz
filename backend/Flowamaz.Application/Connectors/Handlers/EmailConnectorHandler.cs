using System.Net;
using System.Text.Json;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// Sends a transactional email through the platform email service (Resend, noreply@ sender).
/// Input: { to, subject, body, is_html? }. Plain-text bodies are HTML-encoded before wrapping so
/// workflow data cannot inject markup into mail sent from the platform domain.
/// </summary>
public sealed class EmailSendHandler : IConnectorOperationHandler
{
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailSendHandler> _logger;

    public EmailSendHandler(IEmailService emailService, ILogger<EmailSendHandler> logger)
    {
        _emailService = emailService;
        _logger = logger;
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

        var htmlBody = isHtml ? body : $"<pre style=\"font-family:inherit;white-space:pre-wrap\">{WebUtility.HtmlEncode(body)}</pre>";
        var textBody = isHtml ? null : body;

        var sent = await _emailService.SendAsync(to, subject, htmlBody, textBody, ct);
        if (!sent)
            throw new ConnectorOperationException(
                $"{op}: the email provider did not accept the message. It will be retried per the node's retry policy.", retryable: true);

        _logger.LogInformation("[Email:send-email] ExecuteAsync exit");
        return ConnectorHttp.ToDocument(new { sent = true });
    }
}
