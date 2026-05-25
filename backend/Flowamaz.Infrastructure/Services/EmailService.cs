using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flowamaz.Core.Configuration;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Transactional email via Resend (POST https://api.resend.com/emails). Never throws —
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
        ILogger<EmailService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
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
}
