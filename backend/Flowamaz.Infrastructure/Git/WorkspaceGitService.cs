using System.Text;
using System.Text.RegularExpressions;
using Flowamaz.Core.Configuration;
using Flowamaz.Core.Git;
using Flowamaz.Core.Interfaces.Git;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using MergeResult = Flowamaz.Core.Git.MergeResult;

namespace Flowamaz.Infrastructure.Git;

/// <summary>
/// LibGit2Sharp-backed workflow versioning. Each workspace owns a bare repo at
/// <c>{ReposBasePath}/{workspaceId}/workflows.git</c>; every workflow is a single
/// <c>{workflowId}.yaml</c> blob. Bare repos have no working directory, so writes go straight to
/// the object database (blob → tree → commit → ref update). All operations are self-healing: any
/// method opens (initialising if needed) the workspace repo, so a missing repo never throws.
/// </summary>
public sealed class WorkspaceGitService : IWorkspaceGitService
{
    private const string DefaultBranch = "main";
    private const string SystemAuthorName = "Flowamaz Bot";
    private const string SystemAuthorEmail = "team@flowamaz.io";

    private static readonly Regex HunkHeader = new(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.Compiled);
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().Build();
    private static readonly ISerializer YamlSerializer = new SerializerBuilder().Build();

    private readonly string _basePath;
    private readonly ILogger<WorkspaceGitService> _logger;

    public WorkspaceGitService(IOptions<GitOptions> options, ILogger<WorkspaceGitService> logger)
    {
        _logger = logger;
        var configured = options.Value.ReposBasePath;
        _basePath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "git-repos")
            : configured;
        // No I/O in the ctor — the base directory is created lazily on first repo init so a
        // read-only/misconfigured path never fails DI resolution for unrelated requests.
    }

    private string RepoPath(Guid workspaceId) => Path.Combine(_basePath, workspaceId.ToString("D"), "workflows.git");

    private static string WorkflowFile(Guid workflowId) => $"{workflowId:D}.yaml";

    public Task InitialiseRepoAsync(Guid workspaceId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger.LogDebug("WorkspaceGitService.InitialiseRepoAsync enter workspace={WorkspaceId}", workspaceId);
        try
        {
            var path = RepoPath(workspaceId);
            if (Repository.IsValid(path))
            {
                _logger.LogDebug("WorkspaceGitService.InitialiseRepoAsync exit workspace={WorkspaceId} alreadyExists=true", workspaceId);
                return Task.CompletedTask;
            }

            Directory.CreateDirectory(path);
            Repository.Init(path, isBare: true);
            using var repo = new Repository(path);

            var signature = SystemSignature();
            var emptyTree = repo.ObjectDatabase.CreateTree(new TreeDefinition());
            var initial = repo.ObjectDatabase.CreateCommit(
                signature, signature, "chore: initialise workflow repository", emptyTree, Array.Empty<Commit>(), false);

            var branchRef = $"refs/heads/{DefaultBranch}";
            repo.Refs.Add(branchRef, initial.Sha);
            // Point HEAD symbolically at main. Add(name, canonicalRefName) creates a SYMBOLIC ref;
            // UpdateTarget would detach HEAD onto the commit, breaking history walks from HEAD.
            repo.Refs.Add("HEAD", branchRef, allowOverwrite: true);

            _logger.LogInformation("WorkspaceGitService.InitialiseRepoAsync exit workspace={WorkspaceId} sha={Sha}", workspaceId, initial.Sha);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceGitService.InitialiseRepoAsync error workspace={WorkspaceId}", workspaceId);
            throw;
        }
    }

    public Task<CommitResult> CommitWorkflowAsync(
        Guid workspaceId, Guid workflowId, string yamlContent, string message,
        string authorName, string authorEmail, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger.LogDebug("WorkspaceGitService.CommitWorkflowAsync enter workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, workflowId);
        try
        {
            using var repo = OpenRepo(workspaceId);
            var branchRef = CurrentBranchRef(repo);
            var parent = repo.Lookup<Commit>(branchRef);

            var treeDef = parent is null ? new TreeDefinition() : TreeDefinition.From(parent.Tree);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yamlContent));
            var blob = repo.ObjectDatabase.CreateBlob(stream);
            treeDef.Add(WorkflowFile(workflowId), blob, Mode.NonExecutableFile);
            var tree = repo.ObjectDatabase.CreateTree(treeDef);

            var signature = new Signature(
                string.IsNullOrWhiteSpace(authorName) ? SystemAuthorName : authorName,
                string.IsNullOrWhiteSpace(authorEmail) ? SystemAuthorEmail : authorEmail,
                DateTimeOffset.UtcNow);

            var parents = parent is null ? Array.Empty<Commit>() : new[] { parent };
            var commit = repo.ObjectDatabase.CreateCommit(signature, signature, message, tree, parents, false);
            UpdateRef(repo, branchRef, commit.Sha);

            var branchName = branchRef.Replace("refs/heads/", string.Empty);
            _logger.LogInformation("WorkspaceGitService.CommitWorkflowAsync exit workspace={WorkspaceId} workflow={WorkflowId} sha={Sha}", workspaceId, workflowId, commit.Sha);
            return Task.FromResult(new CommitResult(commit.Sha, branchName, null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceGitService.CommitWorkflowAsync error workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, workflowId);
            throw;
        }
    }

    public Task<string> PublishWorkflowAsync(Guid workspaceId, Guid workflowId, int version, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger.LogDebug("WorkspaceGitService.PublishWorkflowAsync enter workspace={WorkspaceId} workflow={WorkflowId} version={Version}", workspaceId, workflowId, version);
        try
        {
            using var repo = OpenRepo(workspaceId);
            var tip = repo.Lookup<Commit>(CurrentBranchRef(repo))
                ?? throw new InvalidOperationException($"Workspace '{workspaceId}' has no commits to tag.");
            var tagName = $"{workflowId:D}/v{version}";
            repo.Tags.Add(tagName, tip, allowOverwrite: true);
            _logger.LogInformation("WorkspaceGitService.PublishWorkflowAsync exit workspace={WorkspaceId} tag={Tag}", workspaceId, tagName);
            return Task.FromResult(tagName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceGitService.PublishWorkflowAsync error workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, workflowId);
            throw;
        }
    }

    public Task<string> GetWorkflowAtCommitAsync(Guid workspaceId, Guid workflowId, string commitSha, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger.LogDebug("WorkspaceGitService.GetWorkflowAtCommitAsync enter workspace={WorkspaceId} workflow={WorkflowId} sha={Sha}", workspaceId, workflowId, commitSha);
        try
        {
            using var repo = OpenRepo(workspaceId);
            var content = ReadFileAtCommit(repo, repo.Lookup<Commit>(commitSha), workflowId);
            _logger.LogDebug("WorkspaceGitService.GetWorkflowAtCommitAsync exit workspace={WorkspaceId} found={Found}", workspaceId, content.Length > 0);
            return Task.FromResult(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceGitService.GetWorkflowAtCommitAsync error workspace={WorkspaceId} workflow={WorkflowId} sha={Sha}", workspaceId, workflowId, commitSha);
            throw;
        }
    }

    public Task<WorkflowDiff> GetDiffAsync(Guid workspaceId, Guid workflowId, string fromSha, string toSha, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger.LogDebug("WorkspaceGitService.GetDiffAsync enter workspace={WorkspaceId} workflow={WorkflowId} from={From} to={To}", workspaceId, workflowId, fromSha, toSha);
        try
        {
            using var repo = OpenRepo(workspaceId);
            var fromCommit = repo.Lookup<Commit>(fromSha);
            var toCommit = repo.Lookup<Commit>(toSha);
            var file = WorkflowFile(workflowId);

            var patch = repo.Diff.Compare<Patch>(fromCommit?.Tree, toCommit?.Tree, new[] { file });
            var lines = ParsePatch(patch.Content);

            var summary = ComputeNodeSummary(
                ReadFileAtCommit(repo, fromCommit, workflowId),
                ReadFileAtCommit(repo, toCommit, workflowId));

            _logger.LogInformation(
                "WorkspaceGitService.GetDiffAsync exit workspace={WorkspaceId} workflow={WorkflowId} added={A} removed={R} modified={M}",
                workspaceId, workflowId, summary.AddedNodes.Count, summary.RemovedNodes.Count, summary.ModifiedNodes.Count);
            return Task.FromResult(new WorkflowDiff(fromSha, toSha, lines, summary));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceGitService.GetDiffAsync error workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, workflowId);
            throw;
        }
    }

    public Task<List<WorkflowCommit>> GetHistoryAsync(Guid workspaceId, Guid workflowId, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger.LogDebug("WorkspaceGitService.GetHistoryAsync enter workspace={WorkspaceId} workflow={WorkflowId} limit={Limit}", workspaceId, workflowId, limit);
        try
        {
            using var repo = OpenRepo(workspaceId);
            var file = WorkflowFile(workflowId);

            // QueryBy(path) is unreliable on bare repos (it follows the working HEAD), so walk the
            // commit graph from HEAD and keep commits where this file changed vs its first parent.
            // Topological order (child before parent) is deterministic even when commit timestamps
            // collide at second-resolution — a plain Time sort would order same-second commits arbitrarily.
            var walk = repo.Commits.QueryBy(new CommitFilter
            {
                IncludeReachableFrom = repo.Refs.Head,
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
            });

            var history = new List<WorkflowCommit>();
            foreach (var commit in walk)
            {
                if (commit[file]?.Target is not Blob blob) continue;

                var parentBlob = commit.Parents.FirstOrDefault()?[file]?.Target as Blob;
                if (parentBlob is not null && parentBlob.Id == blob.Id) continue; // unchanged here

                history.Add(new WorkflowCommit(
                    commit.Sha, commit.Sha[..7], commit.MessageShort,
                    commit.Author.Name, commit.Author.Email, commit.Author.When.UtcDateTime));
                if (history.Count >= limit) break;
            }

            _logger.LogInformation("WorkspaceGitService.GetHistoryAsync exit workspace={WorkspaceId} workflow={WorkflowId} count={Count}", workspaceId, workflowId, history.Count);
            return Task.FromResult(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceGitService.GetHistoryAsync error workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, workflowId);
            throw;
        }
    }

    public Task CreateBranchAsync(Guid workspaceId, string branchName, string? fromSha = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger.LogDebug("WorkspaceGitService.CreateBranchAsync enter workspace={WorkspaceId} branch={Branch}", workspaceId, branchName);
        try
        {
            using var repo = OpenRepo(workspaceId);
            var target = fromSha is null ? repo.Lookup<Commit>(CurrentBranchRef(repo)) : repo.Lookup<Commit>(fromSha);
            if (target is null) throw new InvalidOperationException($"Cannot branch '{branchName}' — no source commit found.");
            repo.Refs.Add($"refs/heads/{branchName}", target.Sha, allowOverwrite: false);
            _logger.LogInformation("WorkspaceGitService.CreateBranchAsync exit workspace={WorkspaceId} branch={Branch} sha={Sha}", workspaceId, branchName, target.Sha);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceGitService.CreateBranchAsync error workspace={WorkspaceId} branch={Branch}", workspaceId, branchName);
            throw;
        }
    }

    public Task CheckoutBranchAsync(Guid workspaceId, string branchName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger.LogDebug("WorkspaceGitService.CheckoutBranchAsync enter workspace={WorkspaceId} branch={Branch}", workspaceId, branchName);
        try
        {
            using var repo = OpenRepo(workspaceId);
            var branchRef = $"refs/heads/{branchName}";
            if (repo.Refs[branchRef] is null) throw new InvalidOperationException($"Branch '{branchName}' does not exist.");
            // Bare repo: no working tree to update — repointing HEAD's symbolic target is the checkout.
            repo.Refs.Add("HEAD", branchRef, allowOverwrite: true);
            _logger.LogInformation("WorkspaceGitService.CheckoutBranchAsync exit workspace={WorkspaceId} branch={Branch}", workspaceId, branchName);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceGitService.CheckoutBranchAsync error workspace={WorkspaceId} branch={Branch}", workspaceId, branchName);
            throw;
        }
    }

    public Task<MergeResult> MergeBranchAsync(Guid workspaceId, string sourceBranch, string targetBranch, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger.LogDebug("WorkspaceGitService.MergeBranchAsync enter workspace={WorkspaceId} source={Source} target={Target}", workspaceId, sourceBranch, targetBranch);
        try
        {
            using var repo = OpenRepo(workspaceId);
            var source = repo.Lookup<Commit>($"refs/heads/{sourceBranch}");
            var target = repo.Lookup<Commit>($"refs/heads/{targetBranch}");
            if (source is null || target is null)
                return Task.FromResult(new MergeResult(false, null, false, Array.Empty<string>()));

            var merge = repo.ObjectDatabase.MergeCommits(target, source, new MergeTreeOptions());
            if (merge.Status == MergeTreeStatus.Conflicts)
            {
                var conflicts = merge.Conflicts
                    .Select(c => c.Ours?.Path ?? c.Theirs?.Path ?? c.Ancestor?.Path ?? "unknown")
                    .ToList();
                _logger.LogWarning("WorkspaceGitService.MergeBranchAsync conflicts workspace={WorkspaceId} count={Count}", workspaceId, conflicts.Count);
                return Task.FromResult(new MergeResult(false, null, true, conflicts));
            }

            var signature = SystemSignature();
            var mergeCommit = repo.ObjectDatabase.CreateCommit(
                signature, signature, $"Merge {sourceBranch} into {targetBranch}", merge.Tree, new[] { target, source }, false);
            UpdateRef(repo, $"refs/heads/{targetBranch}", mergeCommit.Sha);

            _logger.LogInformation("WorkspaceGitService.MergeBranchAsync exit workspace={WorkspaceId} sha={Sha}", workspaceId, mergeCommit.Sha);
            return Task.FromResult(new MergeResult(true, mergeCommit.Sha, false, Array.Empty<string>()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceGitService.MergeBranchAsync error workspace={WorkspaceId} source={Source} target={Target}", workspaceId, sourceBranch, targetBranch);
            throw;
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private Repository OpenRepo(Guid workspaceId)
    {
        var path = RepoPath(workspaceId);
        if (!Repository.IsValid(path))
        {
            // Self-heal: a workspace created before Git versioning (or a failed init) gets a repo now.
            InitialiseRepoAsync(workspaceId).GetAwaiter().GetResult();
        }
        return new Repository(path);
    }

    private static Signature SystemSignature() => new(SystemAuthorName, SystemAuthorEmail, DateTimeOffset.UtcNow);

    private static string CurrentBranchRef(Repository repo) =>
        repo.Refs.Head is SymbolicReference sym ? sym.TargetIdentifier : $"refs/heads/{DefaultBranch}";

    private static void UpdateRef(Repository repo, string canonicalName, string sha)
    {
        if (repo.Refs[canonicalName] is null) repo.Refs.Add(canonicalName, sha);
        else repo.Refs.UpdateTarget(canonicalName, sha);
    }

    private static string ReadFileAtCommit(Repository repo, Commit? commit, Guid workflowId)
    {
        if (commit is null) return string.Empty;
        var entry = commit[WorkflowFile(workflowId)];
        return entry?.Target is Blob blob ? blob.GetContentText() : string.Empty;
    }

    private static List<WorkflowDiffLine> ParsePatch(string patchContent)
    {
        var lines = new List<WorkflowDiffLine>();
        if (string.IsNullOrEmpty(patchContent)) return lines;

        int oldLine = 0, newLine = 0;
        foreach (var raw in patchContent.Split('\n'))
        {
            if (raw.Length == 0) continue;

            var hunk = HunkHeader.Match(raw);
            if (hunk.Success)
            {
                oldLine = int.Parse(hunk.Groups[1].Value);
                newLine = int.Parse(hunk.Groups[2].Value);
                continue;
            }

            // Skip file headers — only hunk body lines carry +/-/space prefixes we care about.
            if (raw.StartsWith("diff ", StringComparison.Ordinal) ||
                raw.StartsWith("index ", StringComparison.Ordinal) ||
                raw.StartsWith("--- ", StringComparison.Ordinal) ||
                raw.StartsWith("+++ ", StringComparison.Ordinal) ||
                raw.StartsWith("new file", StringComparison.Ordinal) ||
                raw.StartsWith("deleted file", StringComparison.Ordinal) ||
                raw.StartsWith(@"\ No newline", StringComparison.Ordinal))
            {
                continue;
            }

            var marker = raw[0];
            var text = raw[1..].TrimEnd('\r');
            switch (marker)
            {
                case '+':
                    lines.Add(new WorkflowDiffLine("added", text, null, newLine++));
                    break;
                case '-':
                    lines.Add(new WorkflowDiffLine("removed", text, oldLine++, null));
                    break;
                default:
                    lines.Add(new WorkflowDiffLine("context", text, oldLine++, newLine++));
                    break;
            }
        }

        return lines;
    }

    private static WorkflowDiffSummary ComputeNodeSummary(string fromYaml, string toYaml)
    {
        var from = ExtractNodes(fromYaml);
        var to = ExtractNodes(toYaml);

        var added = to.Keys.Where(k => !from.ContainsKey(k)).OrderBy(k => k).ToList();
        var removed = from.Keys.Where(k => !to.ContainsKey(k)).OrderBy(k => k).ToList();
        var modified = to.Keys
            .Where(k => from.TryGetValue(k, out var before) && before != to[k])
            .OrderBy(k => k)
            .ToList();

        return new WorkflowDiffSummary(added, removed, modified);
    }

    /// <summary>Maps each node id to its canonical serialised form so modified nodes can be detected.</summary>
    private static Dictionary<string, string> ExtractNodes(string yaml)
    {
        var map = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(yaml)) return map;

        try
        {
            var root = YamlDeserializer.Deserialize<Dictionary<object, object>>(yaml);
            if (root is null || !root.TryGetValue("nodes", out var nodesObj) || nodesObj is not IEnumerable<object> nodes)
                return map;

            foreach (var node in nodes)
            {
                if (node is Dictionary<object, object> dict &&
                    dict.TryGetValue("id", out var idObj) && idObj is not null)
                {
                    map[idObj.ToString()!] = YamlSerializer.Serialize(dict);
                }
            }
        }
        catch (YamlDotNet.Core.YamlException)
        {
            // Malformed YAML at one revision → treat it as having no parseable nodes for the summary.
        }

        return map;
    }
}
