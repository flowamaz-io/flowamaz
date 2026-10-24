using System.Text;
using System.Text.Json;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// Microsoft 365 via Microsoft Graph (/me endpoints), using the workspace's Microsoft OAuth
/// credential. Token refresh is handled by the credential resolver before the handler runs.
/// </summary>
public abstract class Microsoft365HandlerBase : IConnectorOperationHandler
{
    public const string HttpClientName = "microsoft-graph";
    private const string Graph = "https://graph.microsoft.com/v1.0";
    private readonly IHttpClientFactory _httpClientFactory;

    protected Microsoft365HandlerBase(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        Logger = logger;
    }

    protected ILogger Logger { get; }
    public string ConnectorId => "microsoft-365";
    public abstract string OperationId { get; }
    protected string Op => $"microsoft-365/{OperationId}";

    public abstract Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct);

    protected async Task<JsonDocument> SendAsync(HttpRequestMessage request, ConnectorCredential? credential, CancellationToken ct)
    {
        var cred = ConnectorInput.RequireCredential(credential, "Microsoft 365", Op);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cred.AccessToken);
        using var client = _httpClientFactory.CreateClient(HttpClientName);
        return await ConnectorHttp.SendAsync(client, request, Op, ct);
    }

    protected static string Url(string path) => Graph + path;

    protected static string[] Addresses(string csv, string field, string op)
    {
        var addresses = csv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (addresses.Length == 0)
            throw new ConnectorOperationException($"{op}: '{field}' needs at least one email address.");
        foreach (var a in addresses)
            if (!System.Net.Mail.MailAddress.TryCreate(a, out _))
                throw new ConnectorOperationException($"{op}: '{a}' in '{field}' is not a valid email address.");
        return addresses;
    }
}

public sealed class Microsoft365SendMailHandler : Microsoft365HandlerBase
{
    public Microsoft365SendMailHandler(IHttpClientFactory f, ILogger<Microsoft365SendMailHandler> l) : base(f, l) { }
    public override string OperationId => "send-mail";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[M365:send-mail] ExecuteAsync entry");
        var root = input.RootElement;
        var message = new
        {
            subject = ConnectorInput.Required(root, "subject", Op),
            body = new { contentType = ConnectorInput.Bool(root, "is_html") ? "HTML" : "Text", content = ConnectorInput.Required(root, "body", Op) },
            toRecipients = Addresses(ConnectorInput.Required(root, "to", Op), "to", Op)
                .Select(a => new { emailAddress = new { address = a } }).ToArray(),
        };

        using var request = ConnectorHttp.Json(HttpMethod.Post, Url("/me/sendMail"), new { message, saveToSentItems = true });
        using var _ = await SendAsync(request, credential, ct); // 202 Accepted, empty body
        Logger.LogInformation("[M365:send-mail] ExecuteAsync exit");
        return ConnectorHttp.ToDocument(new { sent = true });
    }
}

public sealed class Microsoft365CreateEventHandler : Microsoft365HandlerBase
{
    public Microsoft365CreateEventHandler(IHttpClientFactory f, ILogger<Microsoft365CreateEventHandler> l) : base(f, l) { }
    public override string OperationId => "create-calendar-event";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[M365:create-calendar-event] ExecuteAsync entry");
        var root = input.RootElement;
        var timezone = ConnectorInput.Optional(root, "timezone") ?? "UTC";
        var start = ParseDate(ConnectorInput.Required(root, "start", Op), "start");
        var end = ParseDate(ConnectorInput.Required(root, "end", Op), "end");
        if (end <= start)
            throw new ConnectorOperationException($"{Op}: 'end' must be after 'start'.");

        var attendeesCsv = ConnectorInput.Optional(root, "attendees");
        var payload = new Dictionary<string, object?>
        {
            ["subject"] = ConnectorInput.Required(root, "subject", Op),
            ["start"] = new { dateTime = start.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = timezone },
            ["end"] = new { dateTime = end.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = timezone },
            ["body"] = new { contentType = "Text", content = ConnectorInput.Optional(root, "body") ?? string.Empty },
        };
        if (!string.IsNullOrWhiteSpace(attendeesCsv))
        {
            payload["attendees"] = Addresses(attendeesCsv, "attendees", Op)
                .Select(a => new { emailAddress = new { address = a }, type = "required" }).ToArray();
        }

        using var request = ConnectorHttp.Json(HttpMethod.Post, Url("/me/events"), payload);
        var result = await SendAsync(request, credential, ct);
        Logger.LogInformation("[M365:create-calendar-event] ExecuteAsync exit");
        return result;
    }

    private DateTime ParseDate(string value, string field) =>
        DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            ? d
            : throw new ConnectorOperationException($"{Op}: '{field}' must be an ISO 8601 date-time such as 2026-11-03T09:00:00, got '{value}'.");
}

public sealed class Microsoft365UploadFileHandler : Microsoft365HandlerBase
{
    public Microsoft365UploadFileHandler(IHttpClientFactory f, ILogger<Microsoft365UploadFileHandler> l) : base(f, l) { }
    public override string OperationId => "upload-file";

    /// <summary>Graph simple upload limit.</summary>
    private const int MaxBytes = 4 * 1024 * 1024;

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[M365:upload-file] ExecuteAsync entry");
        var root = input.RootElement;
        var path = ConnectorInput.Required(root, "path", Op);
        var content = ConnectorInput.Required(root, "content", Op);

        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".."))
            throw new ConnectorOperationException($"{Op}: 'path' must be a OneDrive path like 'Reports/summary.txt' without '.' or '..' segments.");
        var bytes = Encoding.UTF8.GetBytes(content);
        if (bytes.Length > MaxBytes)
            throw new ConnectorOperationException($"{Op}: content is {bytes.Length / 1024} KB; the limit is 4 MB per file.");

        var escaped = string.Join('/', segments.Select(Uri.EscapeDataString));
        using var request = new HttpRequestMessage(HttpMethod.Put, Url($"/me/drive/root:/{escaped}:/content"))
        {
            Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("text/plain") { CharSet = "utf-8" } } },
        };
        var result = await SendAsync(request, credential, ct);
        Logger.LogInformation("[M365:upload-file] ExecuteAsync exit");
        return result;
    }
}
