using System.Net.Http.Headers;
using System.Text.Json;
using Flowamaz.Core.Interfaces.Library;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services.Library;

/// <summary>
/// Reads a Library repository through the GitHub REST API: the branch's head commit, its tree, then each
/// <c>connector.yaml</c> (with CHANGELOG.md) or <c>template.yaml</c> (with workflow.yaml) folder at that commit.
/// Configuration: <c>Library:ConnectorsRepo</c>, <c>Library:TemplatesRepo</c> (owner/repo),
/// <c>Library:Branch</c> (default main), <c>Library:GitHubToken</c> (optional; raises the API rate limit).
/// </summary>
public sealed class GitHubLibrarySource : ILibrarySource
{
    public const string HttpClientName = "library-github";
    internal const int MaxFolders = 500;
    internal const int MaxFileBytes = 512 * 1024;

    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<GitHubLibrarySource> _log;

    public GitHubLibrarySource(IHttpClientFactory http, IConfiguration config, ILogger<GitHubLibrarySource> log)
    {
        _http = http;
        _config = config;
        _log = log;
    }

    public bool IsConfigured(string source) => !string.IsNullOrWhiteSpace(Repository(source));

    private string? Repository(string source) => source switch
    {
        "connectors" => _config["Library:ConnectorsRepo"],
        "templates" => _config["Library:TemplatesRepo"],
        _ => null,
    };

    public async Task<LibrarySnapshot> FetchAsync(string source, CancellationToken ct = default)
    {
        var repo = Repository(source)?.Trim()
            ?? throw new InvalidOperationException($"No repository is configured for the {source} Library. Set Library:{(source == "connectors" ? "ConnectorsRepo" : "TemplatesRepo")} to owner/repo.");
        var branch = _config["Library:Branch"] is { Length: > 0 } b ? b : "main";
        _log.LogInformation("GitHubLibrarySource.FetchAsync entry source={Source} repo={Repo} branch={Branch}", source, repo, branch);

        var client = Client();
        var sha = await ShaAsync(client, repo, branch, ct);
        using var tree = await GetJsonAsync(client, $"repos/{repo}/git/trees/{sha}?recursive=1", ct);
        if (tree.RootElement.TryGetProperty("truncated", out var t) && t.GetBoolean())
            throw new InvalidOperationException($"{repo} is too large to read in one tree listing. Split the Library into smaller repositories.");

        var files = tree.RootElement.GetProperty("tree").EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "blob")
            .Select(e => (Path: e.GetProperty("path").GetString()!, Size: e.TryGetProperty("size", out var s) ? s.GetInt64() : 0))
            .ToList();
        // The templates repository also holds forms/ (form.yaml) and packs/ (pack.yaml) folders (§23.2).
        var manifests = source == "connectors"
            ? new Dictionary<string, string[]> { ["connector.yaml"] = ["CHANGELOG.md"] }
            : new Dictionary<string, string[]> { ["template.yaml"] = ["workflow.yaml", "CHANGELOG.md"], ["form.yaml"] = [], ["pack.yaml"] = [] };
        var folders = files.Where(f => manifests.ContainsKey(Path.GetFileName(f.Path)))
            .Select(f => (Folder: Path.GetDirectoryName(f.Path)!.Replace('\\', '/'), Manifest: Path.GetFileName(f.Path)))
            .Where(d => d.Folder.Length > 0)
            .Take(MaxFolders + 1)
            .ToList();
        if (folders.Count > MaxFolders)
            throw new InvalidOperationException($"{repo} has more than {MaxFolders} {source}. Raise GitHubLibrarySource.MaxFolders or split the repository.");

        var sizes = files.ToDictionary(f => f.Path, f => f.Size);
        var items = new List<LibrarySourceItem>();
        foreach (var (folder, manifestName) in folders)
        {
            var contents = new Dictionary<string, string>();
            foreach (var name in manifests[manifestName].Prepend(manifestName))
            {
                var path = $"{folder}/{name}";
                if (!sizes.TryGetValue(path, out var size)) continue;
                if (size > MaxFileBytes)
                {
                    _log.LogWarning("GitHubLibrarySource.FetchAsync skipping {Path}: {Size} bytes is over the limit", path, size);
                    continue;
                }
                contents[name] = await RawAsync(client, repo, path, sha, ct);
            }
            items.Add(new LibrarySourceItem(folder, contents));
        }

        _log.LogInformation("GitHubLibrarySource.FetchAsync exit source={Source} sha={Sha} folders={Folders}", source, sha, items.Count);
        return new LibrarySnapshot(repo, sha, items);
    }

    private HttpClient Client()
    {
        var client = _http.CreateClient(HttpClientName);
        client.BaseAddress ??= new Uri("https://api.github.com/");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Flowamaz-Library-Sync");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        if (_config["Library:GitHubToken"] is { Length: > 0 } token)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> ShaAsync(HttpClient client, string repo, string branch, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(client, $"repos/{repo}/commits/{Uri.EscapeDataString(branch)}", ct);
        return doc.RootElement.GetProperty("sha").GetString()
               ?? throw new InvalidOperationException($"GitHub returned no commit for {repo}@{branch}.");
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GitHub answered {(int)response.StatusCode} for {url}. Check the repository name, the branch and Library:GitHubToken.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static async Task<string> RawAsync(HttpClient client, string repo, string path, string sha, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"repos/{repo}/contents/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}?ref={sha}");
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/vnd.github.raw");
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GitHub answered {(int)response.StatusCode} for {repo}/{path}.");
        return await response.Content.ReadAsStringAsync(ct);
    }
}
