using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Opens a PR against flowamaz-io/connectors via the GitHub REST API: branch off the default
/// branch, commit the submitted manifest under <c>community/{name}/manifest.yaml</c>, open the PR.
/// Requires the <c>GITHUB_CONNECTORS_TOKEN</c> credential (GitHub App installation token).
/// </summary>
public sealed class ConnectorSubmissionPrService : IConnectorSubmissionPrService
{
    public const string HttpClientName = "github-connectors-pr";
    private const string Owner = "flowamaz-io";
    private const string Repo = "connectors";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConnectorSubmissionPrService> _logger;

    public ConnectorSubmissionPrService(
        IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<ConnectorSubmissionPrService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> CreatePullRequestAsync(
        string connectorName, string manifestYaml, string submitterOrgId, CancellationToken cancellationToken = default)
    {
        var token = _configuration["GITHUB_CONNECTORS_TOKEN"]
            ?? throw new InvalidOperationException(
                "Community connector submissions require GITHUB_CONNECTORS_TOKEN to be configured. " +
                "Ask a platform admin to set the GitHub App installation token.");

        var slug = Slugify(connectorName);
        var branch = $"submission/{slug}-{submitterOrgId[..Math.Min(8, submitterOrgId.Length)]}";
        var path = $"community/{slug}/manifest.yaml";

        using var http = CreateClient(token);

        var defaultBranch = await GetDefaultBranchAsync(http, cancellationToken);
        var baseSha = await GetBranchShaAsync(http, defaultBranch, cancellationToken);
        await CreateBranchAsync(http, branch, baseSha, cancellationToken);
        await PutFileAsync(http, path, branch, manifestYaml, connectorName, cancellationToken);
        var prUrl = await OpenPullRequestAsync(http, branch, defaultBranch, connectorName, cancellationToken);

        _logger.LogInformation("ConnectorSubmissionPrService.CreatePullRequestAsync ok connector={Name} pr={Pr}", connectorName, prUrl);
        return prUrl;
    }

    private HttpClient CreateClient(string token)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Flowamaz/1.0");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private static async Task<string> GetDefaultBranchAsync(HttpClient http, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(http, $"https://api.github.com/repos/{Owner}/{Repo}", ct);
        return doc.RootElement.GetProperty("default_branch").GetString() ?? "main";
    }

    private static async Task<string> GetBranchShaAsync(HttpClient http, string branch, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(http, $"https://api.github.com/repos/{Owner}/{Repo}/git/ref/heads/{branch}", ct);
        return doc.RootElement.GetProperty("object").GetProperty("sha").GetString()!;
    }

    private static async Task CreateBranchAsync(HttpClient http, string branch, string sha, CancellationToken ct)
    {
        var resp = await http.PostAsJsonAsync(
            $"https://api.github.com/repos/{Owner}/{Repo}/git/refs",
            new { @ref = $"refs/heads/{branch}", sha }, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task PutFileAsync(HttpClient http, string path, string branch, string content, string name, CancellationToken ct)
    {
        var resp = await http.PutAsJsonAsync(
            $"https://api.github.com/repos/{Owner}/{Repo}/contents/{path}",
            new
            {
                message = $"Add community connector: {name}",
                content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
                branch,
            }, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task<string> OpenPullRequestAsync(HttpClient http, string head, string @base, string name, CancellationToken ct)
    {
        var resp = await http.PostAsJsonAsync(
            $"https://api.github.com/repos/{Owner}/{Repo}/pulls",
            new
            {
                title = $"Community connector: {name}",
                head,
                @base,
                body = $"Automated submission of the **{name}** community connector via the Flowamaz marketplace.",
            }, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("html_url").GetString()!;
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    private static string Slugify(string name) =>
        new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-');
}
