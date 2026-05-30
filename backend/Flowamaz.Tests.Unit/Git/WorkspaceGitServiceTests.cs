using Flowamaz.Core.Configuration;
using Flowamaz.Infrastructure.Git;
using FluentAssertions;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Flowamaz.Tests.Unit.Git;

/// <summary>
/// WorkspaceGitService against real bare repos in a throwaway temp directory (prompt 05-01).
/// Verifies repo init, commit round-trip, historical reconstruction, node-level diff, and tagging.
/// </summary>
public sealed class WorkspaceGitServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "flowamaz-git-tests", Guid.NewGuid().ToString("N"));
    private readonly WorkspaceGitService _git;

    private const string YamlV1 = """
        workflow: { id: w, version: v1, name: Orders }
        nodes:
          - { id: start, type: Trigger }
          - { id: a, type: Action, label: First }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: a }
          - { id: e2, from: a, to: done }
        """;

    // v2: node 'a' modified (label changed), node 'b' added, node 'done' kept.
    private const string YamlV2 = """
        workflow: { id: w, version: v1, name: Orders }
        nodes:
          - { id: start, type: Trigger }
          - { id: a, type: Action, label: Renamed }
          - { id: b, type: Action, label: Second }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: a }
          - { id: e2, from: a, to: b }
          - { id: e3, from: b, to: done }
        """;

    public WorkspaceGitServiceTests()
    {
        var options = Options.Create(new GitOptions { ReposBasePath = _tempRoot });
        _git = new WorkspaceGitService(options, NullLogger<WorkspaceGitService>.Instance);
    }

    [Fact]
    public async Task InitialiseRepo_creates_bare_repo_with_main_branch()
    {
        var workspaceId = Guid.NewGuid();
        await _git.InitialiseRepoAsync(workspaceId);

        var path = Path.Combine(_tempRoot, workspaceId.ToString("D"), "workflows.git");
        Repository.IsValid(path).Should().BeTrue();

        using var repo = new Repository(path);
        repo.Info.IsBare.Should().BeTrue();
        repo.Refs["refs/heads/main"].Should().NotBeNull();
    }

    [Fact]
    public async Task InitialiseRepo_is_idempotent()
    {
        var workspaceId = Guid.NewGuid();
        await _git.InitialiseRepoAsync(workspaceId);
        var act = async () => await _git.InitialiseRepoAsync(workspaceId);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CommitWorkflow_stores_file_retrievable_at_returned_sha()
    {
        var workspaceId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();

        var result = await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV1, "Create Orders", "Dev", "dev@flowamaz.io");

        result.CommitSha.Should().NotBeNullOrWhiteSpace();
        result.BranchName.Should().Be("main");

        var roundTrip = await _git.GetWorkflowAtCommitAsync(workspaceId, workflowId, result.CommitSha);
        roundTrip.Should().Be(YamlV1);
    }

    [Fact]
    public async Task GetWorkflowAtCommit_returns_historical_yaml_not_current()
    {
        var workspaceId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();

        var v1 = await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV1, "v1", "Dev", "dev@flowamaz.io");
        var v2 = await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV2, "v2", "Dev", "dev@flowamaz.io");

        (await _git.GetWorkflowAtCommitAsync(workspaceId, workflowId, v1.CommitSha)).Should().Be(YamlV1);
        (await _git.GetWorkflowAtCommitAsync(workspaceId, workflowId, v2.CommitSha)).Should().Be(YamlV2);
    }

    [Fact]
    public async Task GetDiff_detects_added_removed_and_modified_nodes()
    {
        var workspaceId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();

        var v1 = await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV1, "v1", "Dev", "dev@flowamaz.io");
        var v2 = await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV2, "v2", "Dev", "dev@flowamaz.io");

        var diff = await _git.GetDiffAsync(workspaceId, workflowId, v1.CommitSha, v2.CommitSha);

        diff.Summary.AddedNodes.Should().Contain("b");
        diff.Summary.ModifiedNodes.Should().Contain("a");
        diff.Summary.RemovedNodes.Should().BeEmpty();
        diff.Lines.Should().Contain(l => l.Type == "added");
        diff.Lines.Should().Contain(l => l.Type == "removed");
    }

    [Fact]
    public async Task GetHistory_returns_commits_newest_first()
    {
        var workspaceId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();

        await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV1, "first", "Dev", "dev@flowamaz.io");
        await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV2, "second", "Dev", "dev@flowamaz.io");

        var history = await _git.GetHistoryAsync(workspaceId, workflowId, 50);

        history.Should().HaveCount(2);
        history[0].Message.Should().Be("second");
        history[0].ShortSha.Should().HaveLength(7);
    }

    // Branch/merge coverage (prompt 05-01 Git API on bare repos) ─────────────────

    // Same workflow as YamlV1 but the metadata 'name' differs — used to force a same-line
    // merge conflict between two branches that both edit only that line.
    private const string YamlNameFeature = """
        workflow: { id: w, version: v1, name: FeatureName }
        nodes:
          - { id: start, type: Trigger }
          - { id: a, type: Action, label: First }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: a }
          - { id: e2, from: a, to: done }
        """;

    private const string YamlNameMain = """
        workflow: { id: w, version: v1, name: MainlineName }
        nodes:
          - { id: start, type: Trigger }
          - { id: a, type: Action, label: First }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: a }
          - { id: e2, from: a, to: done }
        """;

    [Fact]
    public async Task CreateBranch_from_current_tip_adds_ref_at_head()
    {
        var workspaceId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var tip = await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV1, "v1", "Dev", "dev@flowamaz.io");

        await _git.CreateBranchAsync(workspaceId, "dev");

        var path = Path.Combine(_tempRoot, workspaceId.ToString("D"), "workflows.git");
        using var repo = new Repository(path);
        repo.Refs["refs/heads/dev"]!.TargetIdentifier.Should().Be(tip.CommitSha);
    }

    [Fact]
    public async Task CreateBranch_from_explicit_sha_points_at_that_commit()
    {
        var workspaceId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var v1 = await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV1, "v1", "Dev", "dev@flowamaz.io");
        await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV2, "v2", "Dev", "dev@flowamaz.io");

        await _git.CreateBranchAsync(workspaceId, "from-v1", v1.CommitSha);

        var path = Path.Combine(_tempRoot, workspaceId.ToString("D"), "workflows.git");
        using var repo = new Repository(path);
        repo.Refs["refs/heads/from-v1"]!.TargetIdentifier.Should().Be(v1.CommitSha);
    }

    [Fact]
    public async Task CreateBranch_from_unknown_sha_throws()
    {
        var workspaceId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV1, "v1", "Dev", "dev@flowamaz.io");

        // Valid-format but nonexistent sha → Lookup returns null → no source commit.
        var act = async () => await _git.CreateBranchAsync(workspaceId, "dev", "0000000000000000000000000000000000000000");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CheckoutBranch_redirects_subsequent_commits_to_that_branch()
    {
        var workspaceId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV1, "v1", "Dev", "dev@flowamaz.io");
        await _git.CreateBranchAsync(workspaceId, "dev");

        await _git.CheckoutBranchAsync(workspaceId, "dev");
        var onDev = await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV2, "on dev", "Dev", "dev@flowamaz.io");

        onDev.BranchName.Should().Be("dev");
    }

    [Fact]
    public async Task CheckoutBranch_missing_branch_throws()
    {
        var workspaceId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV1, "v1", "Dev", "dev@flowamaz.io");

        var act = async () => await _git.CheckoutBranchAsync(workspaceId, "ghost");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task MergeBranch_non_conflicting_edits_creates_merge_commit()
    {
        var workspaceId = Guid.NewGuid();
        var wfA = Guid.NewGuid();
        var wfB = Guid.NewGuid();
        var baseCommit = await _git.CommitWorkflowAsync(workspaceId, wfA, YamlV1, "base", "Dev", "dev@flowamaz.io");

        // feature edits a different workflow file; main edits wfA — disjoint files → clean merge.
        await _git.CreateBranchAsync(workspaceId, "feature", baseCommit.CommitSha);
        await _git.CheckoutBranchAsync(workspaceId, "feature");
        await _git.CommitWorkflowAsync(workspaceId, wfB, YamlV1, "add wfB on feature", "Dev", "dev@flowamaz.io");

        await _git.CheckoutBranchAsync(workspaceId, "main");
        await _git.CommitWorkflowAsync(workspaceId, wfA, YamlV2, "edit wfA on main", "Dev", "dev@flowamaz.io");

        var result = await _git.MergeBranchAsync(workspaceId, "feature", "main");

        result.Success.Should().BeTrue();
        result.HasConflicts.Should().BeFalse();
        result.CommitSha.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task MergeBranch_missing_source_returns_unmerged()
    {
        var workspaceId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV1, "v1", "Dev", "dev@flowamaz.io");

        var result = await _git.MergeBranchAsync(workspaceId, "ghost", "main");

        result.Success.Should().BeFalse();
        result.HasConflicts.Should().BeFalse();
        result.ConflictPaths.Should().BeEmpty();
    }

    [Fact]
    public async Task MergeBranch_same_line_edits_on_both_branches_report_conflict()
    {
        var workspaceId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var baseCommit = await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV1, "base", "Dev", "dev@flowamaz.io");

        await _git.CreateBranchAsync(workspaceId, "feature", baseCommit.CommitSha);
        await _git.CheckoutBranchAsync(workspaceId, "feature");
        await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlNameFeature, "rename on feature", "Dev", "dev@flowamaz.io");

        await _git.CheckoutBranchAsync(workspaceId, "main");
        await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlNameMain, "rename on main", "Dev", "dev@flowamaz.io");

        var result = await _git.MergeBranchAsync(workspaceId, "feature", "main");

        result.Success.Should().BeFalse();
        result.HasConflicts.Should().BeTrue();
        result.ConflictPaths.Should().NotBeEmpty();
    }

    [Fact]
    public async Task PublishWorkflow_creates_tag_with_expected_name()
    {
        var workspaceId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        await _git.CommitWorkflowAsync(workspaceId, workflowId, YamlV1, "v1", "Dev", "dev@flowamaz.io");

        var tagName = await _git.PublishWorkflowAsync(workspaceId, workflowId, 1);

        tagName.Should().Be($"{workflowId:D}/v1");
        var path = Path.Combine(_tempRoot, workspaceId.ToString("D"), "workflows.git");
        using var repo = new Repository(path);
        repo.Tags[tagName].Should().NotBeNull();
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempRoot)) return;
        try
        {
            // .git object files are read-only on some platforms — clear the bit before deleting.
            foreach (var file in Directory.EnumerateFiles(_tempRoot, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }
}
