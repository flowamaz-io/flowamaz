using System.Text.Json;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// Sends email via IEmailService (Resend).
/// Input: { to: string, subject: string, body: string, is_html?: bool }
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

    public async Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct)
    {
        _logger.LogInformation("[Email:send-email] ExecuteAsync entry");

        var root = input.RootElement;
        var to = root.GetProperty("to").GetString() ?? throw new InvalidOperationException("'to' is required.");
        var subject = root.GetProperty("subject").GetString() ?? throw new InvalidOperationException("'subject' is required.");
        var body = root.GetProperty("body").GetString() ?? throw new InvalidOperationException("'body' is required.");
        var isHtml = root.TryGetProperty("is_html", out var htmlEl) && htmlEl.GetBoolean();

        var htmlBody = isHtml ? body : $"<pre>{body}</pre>";
        var textBody = isHtml ? null : body;

        var sent = await _emailService.SendAsync(to, subject, htmlBody, textBody, ct);

        _logger.LogInformation("[Email:send-email] ExecuteAsync exit sent={Sent}", sent);
        return JsonDocument.Parse($"{{\"sent\":{sent.ToString().ToLowerInvariant()}}}");
    }
}
