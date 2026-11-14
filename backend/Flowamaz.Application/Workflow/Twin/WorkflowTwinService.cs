using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Workflow;
using Flowamaz.Core.Workflow.Twin;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Twin;

/// <summary>A finding as the API returns it.</summary>
public sealed record TwinFindingDto(
    string Kind,
    string Severity,
    string NodeId,
    string NodeLabel,
    string Title,
    string Explanation,
    string Fix,
    IReadOnlyDictionary<string, string> Example,
    IReadOnlyList<string> AlsoFailsWith,
    int Cases,
    bool FromRealPayload);

/// <summary>One Twin run of a workflow.</summary>
public sealed record TwinRunDto(
    Guid Id,
    Guid WorkflowDefinitionId,
    string VersionLabel,
    string Trigger,
    string Status,
    int CasesRun,
    int RoutersTested,
    int BreakCount,
    int WarningCount,
    IReadOnlyList<TwinFindingDto> Findings,
    string? ErrorMessage,
    int DurationMs,
    DateTime CompletedAt);

/// <summary>The latest run (null before the first one) and whether the workflow is simulated automatically.</summary>
public sealed record TwinLatestDto(TwinRunDto? Run, bool HasProductionVersion);

/// <summary>A workflow's latest Twin result, for the badge on the workflow list.</summary>
public sealed record TwinSummaryDto(
    Guid WorkflowDefinitionId, string Status, int BreakCount, int WarningCount, int CasesRun, string VersionLabel, DateTime CompletedAt);

/// <summary>
/// Workflow Twin (FUNCTIONAL.md §8.1): simulates a workflow against thousands of generated inputs to find the ones that
/// break it before real users do. The production version is simulated (or the saved draft when there is none), with
/// recent real payloads as extra base inputs and recorded step outputs standing in for connectors. Nothing runs for
/// real: no instances, no connector calls, no AI calls (no cost). Results are kept per workflow and shown as a badge.
/// </summary>
public sealed class WorkflowTwinService
{
    public const int RunsKeptPerWorkflow = 10;
    private const int PastPayloads = 10;
    private const int RecordedRuns = 20;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowVersionRepository _versions;
    private readonly IWorkflowTwinRepository _twins;
    private readonly IUnitOfWork _unitOfWork;
    private readonly SfgParser _parser;
    private readonly ILogger<WorkflowTwinService> _logger;

    public WorkflowTwinService(
        IWorkflowDefinitionRepository definitions,
        IWorkflowVersionRepository versions,
        IWorkflowTwinRepository twins,
        IUnitOfWork unitOfWork,
        SfgParser parser,
        ILogger<WorkflowTwinService> logger)
    {
        _definitions = definitions;
        _versions = versions;
        _twins = twins;
        _unitOfWork = unitOfWork;
        _parser = parser;
        _logger = logger;
    }

    /// <summary>Simulates the workflow now and stores the result.</summary>
    /// <param name="trigger">"manual" or "scheduled".</param>
    public async Task<TwinRunDto> RunAsync(
        Guid workspaceId, Guid workflowDefinitionId, string trigger, Guid? userId = null, CancellationToken ct = default)
    {
        _logger.LogInformation("WorkflowTwinService.RunAsync enter workspace={WorkspaceId} workflow={WorkflowId} trigger={Trigger}",
            workspaceId, workflowDefinitionId, trigger);
        try
        {
            var definition = await _definitions.GetByIdForWorkspaceAsync(workflowDefinitionId, workspaceId, ct)
                ?? throw new WorkflowNotFoundException(workflowDefinitionId);
            var production = await _versions.GetProductionAsync(workflowDefinitionId, workspaceId, ct);
            var yaml = production?.YamlContent ?? definition.YamlContent;

            var run = new WorkflowTwinRun
            {
                WorkspaceId = workspaceId,
                WorkflowDefinitionId = workflowDefinitionId,
                WorkflowVersionId = production?.Id,
                VersionLabel = production is null ? "draft" : "production",
                Trigger = trigger,
                CreatedBy = userId,
            };

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var graph = _parser.Parse(yaml);
                var history = await LoadHistoryAsync(workspaceId, workflowDefinitionId, ct);
                var report = TwinAnalyzer.Analyse(graph, history);
                run.CasesRun = report.CasesRun;
                run.RoutersTested = report.RoutersTested;
                run.BreakCount = report.Breaks;
                run.WarningCount = report.Warnings;
                run.FindingsJson = JsonSerializer.Serialize(report.Findings, Json);
                run.Status = report.Breaks > 0 ? "breaks" : report.Warnings > 0 ? "warnings" : "passed";
            }
            catch (SfgParseException ex)
            {
                run.Status = "failed";
                run.ErrorMessage = $"The Twin couldn't read this workflow: {ex.Message} Fix the workflow (the YAML tab shows the same problem), then run the Twin again.";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A simulator bug must not hide earlier results or break the page: store it as a failed run and log it.
                _logger.LogError(ex, "WorkflowTwinService.RunAsync simulation failed workflow={WorkflowId}", workflowDefinitionId);
                run.Status = "failed";
                run.ErrorMessage = "The Twin hit an internal problem while simulating this workflow, so there's no result this time. "
                    + "It has been logged. Run it again later; if it keeps failing, contact support@flowamaz.io with the workflow's name.";
            }
            stopwatch.Stop();
            run.DurationMs = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);
            run.CompletedAt = DateTime.UtcNow;

            await _twins.AddAsync(run, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await _twins.PruneAsync(workspaceId, workflowDefinitionId, RunsKeptPerWorkflow, ct);

            _logger.LogInformation(
                "WorkflowTwinService.RunAsync exit workflow={WorkflowId} status={Status} cases={Cases} breaks={Breaks} warnings={Warnings} ms={Ms}",
                workflowDefinitionId, run.Status, run.CasesRun, run.BreakCount, run.WarningCount, run.DurationMs);
            return ToDto(run);
        }
        catch (Exception ex) when (ex is not WorkflowNotFoundException and not OperationCanceledException)
        {
            _logger.LogError(ex, "WorkflowTwinService.RunAsync error workflow={WorkflowId}", workflowDefinitionId);
            throw;
        }
    }

    /// <param name="canSeeRunData">
    /// False for Viewers, who can't see run variables: the values of findings that came from a real payload are hidden.
    /// </param>
    public async Task<TwinLatestDto> GetLatestAsync(
        Guid workspaceId, Guid workflowDefinitionId, bool canSeeRunData = true, CancellationToken ct = default)
    {
        _logger.LogDebug("WorkflowTwinService.GetLatestAsync enter workflow={WorkflowId}", workflowDefinitionId);
        try
        {
            _ = await _definitions.GetByIdForWorkspaceAsync(workflowDefinitionId, workspaceId, ct)
                ?? throw new WorkflowNotFoundException(workflowDefinitionId);
            var run = await _twins.GetLatestAsync(workspaceId, workflowDefinitionId, ct);
            var production = await _versions.GetProductionAsync(workflowDefinitionId, workspaceId, ct);
            _logger.LogDebug("WorkflowTwinService.GetLatestAsync exit found={Found}", run is not null);
            return new TwinLatestDto(run is null ? null : ToDto(run, canSeeRunData), production is not null);
        }
        catch (Exception ex) when (ex is not WorkflowNotFoundException and not OperationCanceledException)
        {
            _logger.LogError(ex, "WorkflowTwinService.GetLatestAsync error workflow={WorkflowId}", workflowDefinitionId);
            throw;
        }
    }

    public async Task<IReadOnlyList<TwinSummaryDto>> GetSummaryAsync(Guid workspaceId, CancellationToken ct = default)
    {
        _logger.LogDebug("WorkflowTwinService.GetSummaryAsync enter workspace={WorkspaceId}", workspaceId);
        try
        {
            var runs = await _twins.GetLatestForWorkspaceAsync(workspaceId, ct);
            var result = runs.Select(r => new TwinSummaryDto(
                r.WorkflowDefinitionId, r.Status, r.BreakCount, r.WarningCount, r.CasesRun, r.VersionLabel, r.CompletedAt)).ToList();
            _logger.LogDebug("WorkflowTwinService.GetSummaryAsync exit count={Count}", result.Count);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WorkflowTwinService.GetSummaryAsync error workspace={WorkspaceId}", workspaceId);
            throw;
        }
    }

    /// <summary>Runs the Twin for workflows whose production version changed or whose last run is a day old.</summary>
    public async Task<int> RunDueAsync(int limit, CancellationToken ct = default)
    {
        _logger.LogInformation("WorkflowTwinService.RunDueAsync enter limit={Limit}", limit);
        var due = await _twins.GetDueAsync(DateTime.UtcNow.AddHours(-24), limit, ct);
        var done = 0;
        foreach (var workflow in due)
        {
            try
            {
                await RunAsync(workflow.WorkspaceId, workflow.WorkflowDefinitionId, "scheduled", null, ct);
                done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One workflow failing must not stop the others; it is retried on the next tick.
                _logger.LogError(ex, "WorkflowTwinService.RunDueAsync workflow={WorkflowId} failed", workflow.WorkflowDefinitionId);
            }
        }
        _logger.LogInformation("WorkflowTwinService.RunDueAsync exit due={Due} done={Done}", due.Count, done);
        return done;
    }

    private async Task<TwinHistory> LoadHistoryAsync(Guid workspaceId, Guid workflowDefinitionId, CancellationToken ct)
    {
        var payloads = (await _twins.GetRecentPayloadsAsync(workspaceId, workflowDefinitionId, PastPayloads, ct))
            .Select(ParseJson).Where(p => p is not null).ToList();
        var outputs = (await _twins.GetRecordedOutputsAsync(workspaceId, workflowDefinitionId, RecordedRuns, ct))
            .ToDictionary(kv => kv.Key, kv => ParseJson(kv.Value), StringComparer.Ordinal);
        return new TwinHistory(payloads, outputs);
    }

    private static JsonNode? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json); }
        catch (JsonException) { return null; }
    }

    public const string HiddenValue = "(hidden — Viewers can't see run data)";

    internal static TwinRunDto ToDto(WorkflowTwinRun run, bool canSeeRunData = true)
    {
        var findings = new List<TwinFindingDto>();
        try
        {
            foreach (var f in JsonSerializer.Deserialize<List<TwinFinding>>(run.FindingsJson, Json) ?? [])
            {
                // A real payload's values are run data; Viewers don't see them (the explanation quotes no values for these).
                var example = f.FromRealPayload && !canSeeRunData
                    ? f.Example.ToDictionary(kv => kv.Key, _ => HiddenValue)
                    : f.Example;
                findings.Add(new TwinFindingDto(f.Kind, f.Severity.ToString().ToLowerInvariant(), f.NodeId, f.NodeLabel, f.Title,
                    f.Explanation, f.Fix, example, f.AlsoFailsWith, f.Cases, f.FromRealPayload));
            }
        }
        catch (JsonException)
        {
            // A stored result that no longer reads is shown without findings rather than failing the page.
        }
        return new TwinRunDto(run.Id, run.WorkflowDefinitionId, run.VersionLabel, run.Trigger, run.Status, run.CasesRun,
            run.RoutersTested, run.BreakCount, run.WarningCount, findings, run.ErrorMessage, run.DurationMs, run.CompletedAt);
    }
}
