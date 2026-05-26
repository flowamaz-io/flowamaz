using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>Base for GitHub API handlers. Adds required User-Agent and Bearer auth.</summary>
public abstract class GitHubHandlerBase : IConnectorOperationHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    protected GitHubHandlerBase(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string ConnectorId => "github";
    public abstract string OperationId { get; }

    protected ILogger Logger => _logger;

    protected HttpClient CreateClient() => _httpClientFactory.CreateClient("github-connector");

    protected static void SetAuth(HttpRequestMessage request, string? token)
    {
        request.Headers.Add("User-Agent", "Flowamaz/1.0");
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public abstract Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct);
}

public sealed class GitHubCreateIssueHandler : GitHubHandlerBase
{
    public GitHubCreateIssueHandler(IHttpClientFactory f, ILogger<GitHubCreateIssueHandler> l) : base(f, l) { }
    public override string OperationId => "create-issue";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[GitHub:create-issue] ExecuteAsync entry");

        var root = input.RootElement;
        var owner = root.GetProperty("owner").GetString() ?? throw new InvalidOperationException("'owner' is required.");
        var repo = root.GetProperty("repo").GetString() ?? throw new InvalidOperationException("'repo' is required.");
        var title = root.GetProperty("title").GetString() ?? throw new InvalidOperationException("'title' is required.");
        var body = root.TryGetProperty("body", out var bEl) ? bEl.GetString() : null;

        var labels = root.TryGetProperty("labels", out var lEl) && lEl.ValueKind == JsonValueKind.Array
            ? lEl.EnumerateArray().Select(e => e.GetString()).Where(s => s is not null).ToArray()
            : [];

        var payload = JsonSerializer.Serialize(new { title, body, labels });
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{owner}/{repo}/issues")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        SetAuth(request, null); // token set by sandbox via credential

        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        Logger.LogInformation("[GitHub:create-issue] ExecuteAsync exit status={Status}", (int)response.StatusCode);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(responseBody) ? "{}" : responseBody);
    }
}

public sealed class GitHubGetIssueHandler : GitHubHandlerBase
{
    public GitHubGetIssueHandler(IHttpClientFactory f, ILogger<GitHubGetIssueHandler> l) : base(f, l) { }
    public override string OperationId => "get-issue";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[GitHub:get-issue] ExecuteAsync entry");

        var root = input.RootElement;
        var owner = root.GetProperty("owner").GetString()!;
        var repo = root.GetProperty("repo").GetString()!;
        var issueNumber = root.GetProperty("issue_number").GetInt32();

        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repo}/issues/{issueNumber}");
        SetAuth(request, null);

        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        Logger.LogInformation("[GitHub:get-issue] ExecuteAsync exit");
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(responseBody) ? "{}" : responseBody);
    }
}

public sealed class GitHubCreatePrHandler : GitHubHandlerBase
{
    public GitHubCreatePrHandler(IHttpClientFactory f, ILogger<GitHubCreatePrHandler> l) : base(f, l) { }
    public override string OperationId => "create-pr";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[GitHub:create-pr] ExecuteAsync entry");

        var root = input.RootElement;
        var owner = root.GetProperty("owner").GetString()!;
        var repo = root.GetProperty("repo").GetString()!;
        var title = root.GetProperty("title").GetString()!;
        var head = root.GetProperty("head").GetString()!;
        var base_ = root.GetProperty("base").GetString()!;
        var body = root.TryGetProperty("body", out var bEl) ? bEl.GetString() : null;

        var payload = JsonSerializer.Serialize(new { title, head, @base = base_, body });
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{owner}/{repo}/pulls")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        SetAuth(request, null);

        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        Logger.LogInformation("[GitHub:create-pr] ExecuteAsync exit");
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(responseBody) ? "{}" : responseBody);
    }
}

public sealed class GitHubAddCommentHandler : GitHubHandlerBase
{
    public GitHubAddCommentHandler(IHttpClientFactory f, ILogger<GitHubAddCommentHandler> l) : base(f, l) { }
    public override string OperationId => "add-comment";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[GitHub:add-comment] ExecuteAsync entry");

        var root = input.RootElement;
        var owner = root.GetProperty("owner").GetString()!;
        var repo = root.GetProperty("repo").GetString()!;
        var issueNumber = root.GetProperty("issue_number").GetInt32();
        var body = root.GetProperty("body").GetString()!;

        var payload = JsonSerializer.Serialize(new { body });
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{owner}/{repo}/issues/{issueNumber}/comments")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        SetAuth(request, null);

        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        Logger.LogInformation("[GitHub:add-comment] ExecuteAsync exit");
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(responseBody) ? "{}" : responseBody);
    }
}
