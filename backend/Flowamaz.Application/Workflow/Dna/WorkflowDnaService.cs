using System.Security.Cryptography;
using System.Text;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Dna;

public sealed class WorkflowDnaService : IWorkflowDnaService
{
    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowMetricRepository _metrics;
    private readonly SfgParser _sfgParser;
    private readonly ILogger<WorkflowDnaService> _log;

    private const int CloneSimilarityThreshold = 75;

    public WorkflowDnaService(
        IWorkflowDefinitionRepository definitions,
        IWorkflowMetricRepository metrics,
        SfgParser sfgParser,
        ILogger<WorkflowDnaService> log)
    {
        _definitions = definitions;
        _metrics = metrics;
        _sfgParser = sfgParser;
        _log = log;
    }

    public async Task<WorkflowDna> ComputeDnaAsync(
        Guid workflowDefinitionId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        _log.LogInformation("WorkflowDnaService.ComputeDnaAsync entry id={Id}", workflowDefinitionId);

        var def = await _definitions.GetByIdForWorkspaceAsync(workflowDefinitionId, workspaceId, cancellationToken);
        if (def is null)
            throw new InvalidOperationException($"Workflow {workflowDefinitionId} not found in workspace {workspaceId}.");

        // SfgParser throws SfgParseException on malformed YAML — propagate to caller.
        var graph = await _sfgParser.ParseAsync(def.YamlContent, cancellationToken);

        var nodeTypes = graph.Nodes
            .Select(n => n.Type.ToString().ToLowerInvariant())
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var connectorIds = graph.Nodes
            .Where(n => n.Type == NodeType.Action)
            .Select(n => n.Config.RootElement.TryGetProperty("connector_id", out var c) ? c.GetString() : null)
            .OfType<string>()
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        var hasAi = graph.Nodes.Any(n => n.Type == NodeType.Ai);
        var hasGates = graph.Nodes.Any(n => n.Type == NodeType.HumanGate);
        var hasParallel = graph.Nodes.Any(n => n.Type == NodeType.Parallel || n.Type == NodeType.ForEach);
        var nodeCount = graph.Nodes.Count;
        var edgeCount = graph.Edges.Count;

        var since = DateTime.UtcNow.AddDays(-30);
        var metricList = await _metrics.GetForDefinitionSinceAsync(workflowDefinitionId, workspaceId, since, cancellationToken);
        var totalRuns = metricList.Sum(m => m.RunsTotal);
        var slaBreaches = metricList.Sum(m => m.SlaBreachCount);
        var slaThreshold = metricList.FirstOrDefault(m => m.SlaThresholdMs.HasValue)?.SlaThresholdMs;
        var compliance = totalRuns > 0 ? Math.Round(1m - (decimal)slaBreaches / totalRuns, 4) : 1m;

        var dnaHash = ComputeHash(def.TriggerType.ToString(), nodeTypes, connectorIds, hasAi, hasGates, hasParallel);

        var dna = new WorkflowDna(
            workflowDefinitionId, def.TriggerType.ToString().ToLowerInvariant(),
            nodeTypes, connectorIds, hasAi, hasGates, hasParallel,
            nodeCount, edgeCount, slaThreshold, compliance, dnaHash);

        _log.LogInformation("WorkflowDnaService.ComputeDnaAsync exit hash={Hash}", dnaHash);
        return dna;
    }

    public async Task<IReadOnlyList<WorkflowSimilarity>> FindSimilarAsync(
        Guid workspaceId, WorkflowDna dna, int limit, CancellationToken cancellationToken = default)
    {
        _log.LogInformation("WorkflowDnaService.FindSimilarAsync entry workspaceId={WorkspaceId}", workspaceId);

        var all = await _definitions.GetForWorkspaceAsync(workspaceId, cancellationToken);
        var results = new List<WorkflowSimilarity>();

        foreach (var def in all)
        {
            if (def.Id == dna.WorkflowDefinitionId) continue;

            WorkflowGraph otherGraph;
            try
            {
                otherGraph = await _sfgParser.ParseAsync(def.YamlContent, cancellationToken);
            }
            catch (SfgParseException ex)
            {
                _log.LogWarning("WorkflowDnaService.FindSimilarAsync skipping workflow {Id} — parse error: {Msg}", def.Id, ex.Message);
                continue;
            }

            var otherNodeTypes = otherGraph.Nodes
                .Select(n => n.Type.ToString().ToLowerInvariant())
                .ToHashSet();
            var otherConnectors = otherGraph.Nodes
                .Where(n => n.Type == NodeType.Action)
                .Select(n => n.Config.RootElement.TryGetProperty("connector_id", out var c) ? c.GetString() : null)
                .OfType<string>()
                .Distinct()
                .ToList();
            var otherHasAi = otherGraph.Nodes.Any(n => n.Type == NodeType.Ai);
            var otherHasGates = otherGraph.Nodes.Any(n => n.Type == NodeType.HumanGate);
            var otherHasParallel = otherGraph.Nodes.Any(n => n.Type == NodeType.Parallel || n.Type == NodeType.ForEach);
            var otherTrigger = def.TriggerType.ToString().ToLowerInvariant();

            var score = 0;
            if (dna.TriggerType == otherTrigger) score += 20;
            if (dna.HasHumanGates == otherHasGates) score += 15;
            if (dna.HasAiNodes == otherHasAi) score += 15;
            if (dna.HasParallelBranches == otherHasParallel) score += 10;

            var sharedConnectors = dna.ConnectorIds.Intersect(otherConnectors).ToList();
            var maxConnectors = Math.Max(dna.ConnectorIds.Count, otherConnectors.Count);
            if (maxConnectors > 0) score += (int)Math.Round(20.0 * sharedConnectors.Count / maxConnectors);

            var allTypes = dna.NodeTypes.Union(otherNodeTypes).ToList();
            var sharedTypes = dna.NodeTypes.Intersect(otherNodeTypes).Count();
            if (allTypes.Count > 0) score += (int)Math.Round(20.0 * sharedTypes / allTypes.Count);

            results.Add(new WorkflowSimilarity(def.Id, def.Name, Math.Min(100, score), sharedConnectors));
        }

        var sorted = results.OrderByDescending(r => r.SimilarityScore).Take(limit).ToList();
        _log.LogInformation("WorkflowDnaService.FindSimilarAsync exit count={Count}", sorted.Count);
        return sorted;
    }

    public async Task<CloneSuggestion?> SuggestCloneAsync(
        Guid workspaceId, string partialName, CancellationToken cancellationToken = default)
    {
        _log.LogInformation("WorkflowDnaService.SuggestCloneAsync entry name={Name}", partialName);

        if (string.IsNullOrWhiteSpace(partialName) || partialName.Length < 3)
            return null;

        var all = await _definitions.GetForWorkspaceAsync(workspaceId, cancellationToken);
        var bestScore = 0;
        WorkflowSimilarity? best = null;

        var lowerPartial = partialName.ToLowerInvariant();
        foreach (var def in all)
        {
            var defWords = def.Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var partialWords = lowerPartial.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var shared = defWords.Intersect(partialWords).Count();
            var totalWords = Math.Max(defWords.Length, partialWords.Length);
            var nameScore = totalWords > 0 ? (int)Math.Round(100.0 * shared / totalWords) : 0;

            if (nameScore > bestScore && nameScore >= CloneSimilarityThreshold)
            {
                bestScore = nameScore;
                best = new WorkflowSimilarity(def.Id, def.Name, nameScore, []);
            }
        }

        if (best is null) return null;

        _log.LogInformation("WorkflowDnaService.SuggestCloneAsync exit suggestion={Name} score={Score}", best.Name, best.SimilarityScore);
        return new CloneSuggestion(best.WorkflowDefinitionId, best.Name, best.SimilarityScore);
    }

    private static string ComputeHash(
        string trigger, IReadOnlyList<string> nodeTypes, IReadOnlyList<string> connectors,
        bool hasAi, bool hasGates, bool hasParallel)
    {
        var input = $"{trigger}|{string.Join(',', nodeTypes)}|{string.Join(',', connectors)}|{hasAi}|{hasGates}|{hasParallel}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..16].ToLowerInvariant();
    }
}
