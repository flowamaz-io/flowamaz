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
