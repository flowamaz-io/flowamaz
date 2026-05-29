using Flowamaz.Core.Interfaces.Git;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Flowamaz.Infrastructure.Jobs;

/// <summary>
/// One-time startup backfill (fires once via a StartNow trigger). Workspaces created before Git
/// versioning have no repository; this initialises one for every workspace and commits the current
/// YAML for each of its workflows so history is non-empty going forward. Idempotent — re-running
/// only commits workflows whose HEAD content has drifted, so a normal restart is a fast no-op.
/// </summary>
[DisallowConcurrentExecution]
public sealed class WorkspaceGitInitialisationJob : IJob
{
    private readonly IWorkspaceRepository _workspaces;
    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkspaceGitService _git;
    private readonly ILogger<WorkspaceGitInitialisationJob> _logger;

    public WorkspaceGitInitialisationJob(
        IWorkspaceRepository workspaces,
        IWorkflowDefinitionRepository definitions,
        IWorkspaceGitService git,
        ILogger<WorkspaceGitInitialisationJob> logger)
    {
        _workspaces = workspaces;
        _definitions = definitions;
        _git = git;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        _logger.LogInformation("WorkspaceGitInitialisationJob enter");
        try
        {
            var workspaces = await _workspaces.GetAllAsync(ct);
            var initialised = 0;
            var committed = 0;

            foreach (var workspace in workspaces)
            {
                await _git.InitialiseRepoAsync(workspace.Id, ct);
                initialised++;

                var definitions = await _definitions.GetForWorkspaceAsync(workspace.Id, ct);
                foreach (var def in definitions)
                {
                    if (string.IsNullOrWhiteSpace(def.YamlContent)) continue;

                    var headContent = await _git.GetWorkflowAtCommitAsync(workspace.Id, def.Id, "HEAD", ct);
                    if (headContent == def.YamlContent) continue; // already committed — skip

                    await _git.CommitWorkflowAsync(
                        workspace.Id, def.Id, def.YamlContent,
                        "chore: backfill workflow YAML", string.Empty, string.Empty, ct);
                    committed++;
                }
            }

            _logger.LogInformation(
                "WorkspaceGitInitialisationJob exit workspaces={Workspaces} backfilledWorkflows={Committed}",
                initialised, committed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WorkspaceGitInitialisationJob error during one-time Git backfill");
        }
    }
}
