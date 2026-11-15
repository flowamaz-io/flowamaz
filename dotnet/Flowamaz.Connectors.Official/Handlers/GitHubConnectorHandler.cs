using System.Text.Json;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// Base for GitHub REST API handlers. Authenticates with the workspace's GitHub OAuth token or PAT
/// from the credential; owner/repo are escaped as single path segments.
/// </summary>
public abstract class GitHubHandlerBase : IConnectorOperationHandler
{
    public const string HttpClientName = "github-connector";
    private readonly IHttpClientFactory _httpClientFactory;

    protected GitHubHandlerBase(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        Logger = logger;
    }

    public string ConnectorId => "github";
    public abstract string OperationId { get; }
    protected ILogger Logger { get; }

    public abstract Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct);

    protected async Task<JsonDocument> CallAsync(
        HttpMethod method, string path, object? body, ConnectorCredential? credential, CancellationToken ct)
    {
        var op = $"github/{OperationId}";
        var cred = ConnectorInput.RequireCredential(credential, "GitHub", op);
        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = ConnectorHttp.Json(method, $"https://api.github.com{path}", body, cred.AccessToken);
        request.Headers.Add("User-Agent", "Flowamaz/1.0");
        request.Headers.Add("Accept", "application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return await ConnectorHttp.SendAsync(client, request, op, ct);
    }

    protected (string Owner, string Repo) Repo(JsonElement root) =>
        (ConnectorInput.Segment(ConnectorInput.Required(root, "owner", $"github/{OperationId}")),
         ConnectorInput.Segment(ConnectorInput.Required(root, "repo", $"github/{OperationId}")));

    protected int Number(JsonElement root, string name)
    {
        var raw = ConnectorInput.Required(root, name, $"github/{OperationId}");
        return int.TryParse(raw, out var n) && n > 0
            ? n
            : throw new Flowamaz.Core.Exceptions.ConnectorOperationException(
                $"github/{OperationId}: '{name}' must be a positive number, got '{raw}'.");
    }
}

public sealed class GitHubCreateIssueHandler : GitHubHandlerBase
{
    public GitHubCreateIssueHandler(IHttpClientFactory f, ILogger<GitHubCreateIssueHandler> l) : base(f, l) { }
    public override string OperationId => "create-issue";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[GitHub:create-issue] ExecuteAsync entry");
        var root = input.RootElement;
        var (owner, repo) = Repo(root);
        var title = ConnectorInput.Required(root, "title", "github/create-issue");
        var body = ConnectorInput.Optional(root, "body");
        var labels = root.TryGetProperty("labels", out var l) && l.ValueKind == JsonValueKind.Array
            ? l.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
            : [];

        var result = await CallAsync(HttpMethod.Post, $"/repos/{owner}/{repo}/issues", new { title, body, labels }, credential, ct);
        Logger.LogInformation("[GitHub:create-issue] ExecuteAsync exit");
        return result;
    }
}

public sealed class GitHubGetIssueHandler : GitHubHandlerBase
{
    public GitHubGetIssueHandler(IHttpClientFactory f, ILogger<GitHubGetIssueHandler> l) : base(f, l) { }
    public override string OperationId => "get-issue";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[GitHub:get-issue] ExecuteAsync entry");
        var root = input.RootElement;
        var (owner, repo) = Repo(root);
        var number = Number(root, "issue_number");

        var result = await CallAsync(HttpMethod.Get, $"/repos/{owner}/{repo}/issues/{number}", null, credential, ct);
        Logger.LogInformation("[GitHub:get-issue] ExecuteAsync exit");
        return result;
    }
}

public sealed class GitHubCreatePrHandler : GitHubHandlerBase
{
    public GitHubCreatePrHandler(IHttpClientFactory f, ILogger<GitHubCreatePrHandler> l) : base(f, l) { }
    public override string OperationId => "create-pr";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[GitHub:create-pr] ExecuteAsync entry");
        var root = input.RootElement;
        var (owner, repo) = Repo(root);
        var payload = new
        {
            title = ConnectorInput.Required(root, "title", "github/create-pr"),
            head = ConnectorInput.Required(root, "head", "github/create-pr"),
            @base = ConnectorInput.Required(root, "base", "github/create-pr"),
            body = ConnectorInput.Optional(root, "body"),
        };

        var result = await CallAsync(HttpMethod.Post, $"/repos/{owner}/{repo}/pulls", payload, credential, ct);
        Logger.LogInformation("[GitHub:create-pr] ExecuteAsync exit");
        return result;
    }
}

public sealed class GitHubAddCommentHandler : GitHubHandlerBase
{
    public GitHubAddCommentHandler(IHttpClientFactory f, ILogger<GitHubAddCommentHandler> l) : base(f, l) { }
    public override string OperationId => "add-comment";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[GitHub:add-comment] ExecuteAsync entry");
        var root = input.RootElement;
        var (owner, repo) = Repo(root);
        var number = Number(root, "issue_number");
        var body = ConnectorInput.Required(root, "body", "github/add-comment");

        var result = await CallAsync(HttpMethod.Post, $"/repos/{owner}/{repo}/issues/{number}/comments", new { body }, credential, ct);
        Logger.LogInformation("[GitHub:add-comment] ExecuteAsync exit");
        return result;
    }
}
