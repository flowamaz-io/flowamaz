using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using Flowamaz.Core.Configuration;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Transactional email via Resend (POST https://api.resend.com/emails), or via SMTP when <c>Email:SmtpHost</c> is
/// set (local development sends to Mailpit; see infrastructure/docker-compose.dev.yml). Never throws —
/// missing API key, network errors, and non-2xx responses all log and return false.
/// Caller decides whether to retry; e.g. a registration handler may compensate by surfacing
/// "verification email could not be sent — try resend".
/// </summary>
public sealed class EmailService : IEmailService
{
    public const string HttpClientName = "Resend";
    private const string ResendEndpoint = "https://api.resend.com/emails";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmailOptions _options;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IHttpClientFactory httpClientFactory,
        IOptions<EmailOptions> options,
        IConfiguration configuration,
        ILogger<EmailService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var opt = options.Value;
        // .NET env var convention requires double-underscore (Email__ResendApiKey) for nested keys.
        // Docker Compose sets single-underscore names (RESEND_API_KEY, EMAIL_FROM_ADDRESS),
        // so we check both the bound value and the flat env var name.
        _options = new EmailOptions
        {
            ResendApiKey = NonEmpty(opt.ResendApiKey, configuration["RESEND_API_KEY"]),
            FromAddress  = NonEmpty(opt.FromAddress,  configuration["EMAIL_FROM_ADDRESS"]),
            FromName     = opt.FromName,
            SupportEmail = opt.SupportEmail,
            SecurityEmail = opt.SecurityEmail,
            SmtpHost     = NonEmpty(opt.SmtpHost, configuration["SMTP_HOST"]),
            SmtpPort     = opt.SmtpPort,
            SmtpEnableSsl = opt.SmtpEnableSsl,
        };
        if (string.IsNullOrWhiteSpace(_options.FromAddress)) _options.FromAddress = "noreply@flowamaz.io";
        if (!string.IsNullOrWhiteSpace(_options.SmtpHost))
            _logger.LogWarning(
                "EmailService: sending all email through SMTP {Host}:{Port} (ssl={Ssl}), not Resend. Meant for local development " +
                "(Mailpit); unset Email:SmtpHost / SMTP_HOST in production.",
                _options.SmtpHost, _options.SmtpPort, _options.SmtpEnableSsl);
    }

    private static string NonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v!;
        return string.Empty;
    }

    public async Task<bool> SendAsync(
        string to,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(to);
        ArgumentException.ThrowIfNullOrEmpty(subject);
        ArgumentException.ThrowIfNullOrEmpty(htmlBody);

        _logger.LogDebug("EmailService.SendAsync enter to={To} subject={Subject}", to, subject);

        if (!string.IsNullOrWhiteSpace(_options.SmtpHost))
            return await SendSmtpAsync(to, subject, htmlBody, textBody, cancellationToken);

        if (string.IsNullOrWhiteSpace(_options.ResendApiKey))
        {
            _logger.LogWarning(
                "EmailService.SendAsync — Resend API key not configured. Email to {To} subject '{Subject}' NOT sent. " +
                "Set Email:ResendApiKey (or RESEND_API_KEY env var) in non-dev environments.",
                to, subject);
            return false;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ResendApiKey);

            var payload = new
            {
                from = $"{_options.FromName} <{_options.FromAddress}>",
                to = new[] { to },
                subject,
                html = htmlBody,
                text = textBody,
            };

            using var response = await client.PostAsJsonAsync(ResendEndpoint, payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "EmailService.SendAsync error — Resend returned {StatusCode}. to={To} subject={Subject} body={Body}",
                    (int)response.StatusCode, to, subject, body);
                return false;
            }

            _logger.LogDebug("EmailService.SendAsync exit to={To} subject={Subject} status=ok", to, subject);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "EmailService.SendAsync error to={To} subject={Subject}", to, subject);
            return false;
        }
    }

    private async Task<bool> SendSmtpAsync(string to, string subject, string htmlBody, string? textBody, CancellationToken ct)
    {
        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_options.FromAddress, _options.FromName),
                Subject = subject,
            };
            message.To.Add(to);
            // multipart/alternative: plain text first, HTML last (mail clients show the last part they can render).
            // With the HTML in Body plus an alternate view, the HTML part went out labelled text/plain.
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                string.IsNullOrEmpty(textBody) ? subject : textBody, System.Text.Encoding.UTF8, "text/plain"));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, System.Text.Encoding.UTF8, "text/html"));

            using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort) { EnableSsl = _options.SmtpEnableSsl };
            await client.SendMailAsync(message, ct);
            _logger.LogDebug("EmailService.SendAsync exit to={To} subject={Subject} status=ok via=smtp", to, subject);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "EmailService.SendAsync error via SMTP {Host}:{Port} to={To} subject={Subject}",
                _options.SmtpHost, _options.SmtpPort, to, subject);
            return false;
        }
    }
}
