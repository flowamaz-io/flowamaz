using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Dna;

public sealed partial class WorkflowDnaService : IWorkflowDnaService
{
    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowMetricRepository _metrics;
    private readonly ILogger<WorkflowDnaService> _log;

    private const int CloneSimilarityThreshold = 75;

    public WorkflowDnaService(
        IWorkflowDefinitionRepository definitions,
        IWorkflowMetricRepository metrics,
        ILogger<WorkflowDnaService> log)
    {
        _definitions = definitions;
        _metrics = metrics;
        _log = log;
    }

    public async Task<WorkflowDna> ComputeDnaAsync(
        Guid workflowDefinitionId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        _log.LogInformation("WorkflowDnaService.ComputeDnaAsync entry id={Id}", workflowDefinitionId);

        var def = await _definitions.GetByIdForWorkspaceAsync(workflowDefinitionId, workspaceId, cancellationToken);
        if (def is null)
            throw new InvalidOperationException($"Workflow {workflowDefinitionId} not found in workspace {workspaceId}.");

        var yaml = def.YamlContent;
        var nodeTypes = ExtractNodeTypes(yaml);
        var connectors = ExtractConnectorIds(yaml);
        var hasAi = nodeTypes.Contains("ai");
        var hasGates = nodeTypes.Contains("human-gate");
        var hasParallel = nodeTypes.Contains("parallel") || nodeTypes.Contains("foreach");
        var nodeCount = nodeTypes.Count;
        var edgeCount = CountEdges(yaml);

        // SLA compliance from latest metric
        var since = DateTime.UtcNow.AddDays(-30);
        var metricList = await _metrics.GetForDefinitionSinceAsync(workflowDefinitionId, workspaceId, since, cancellationToken);
        var totalRuns = metricList.Sum(m => m.RunsTotal);
        var slaBreaches = metricList.Sum(m => m.SlaBreachCount);
        var slaThreshold = metricList.FirstOrDefault(m => m.SlaThresholdMs.HasValue)?.SlaThresholdMs;
        var compliance = totalRuns > 0 ? Math.Round(1m - (decimal)slaBreaches / totalRuns, 4) : 1m;

        var dnaHash = ComputeHash(def.TriggerType.ToString(), nodeTypes, connectors, hasAi, hasGates, hasParallel);

        var dna = new WorkflowDna(
            workflowDefinitionId, def.TriggerType.ToString().ToLowerInvariant(),
            nodeTypes, connectors, hasAi, hasGates, hasParallel,
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

            var otherNodeTypes = ExtractNodeTypes(def.YamlContent);
            var otherConnectors = ExtractConnectorIds(def.YamlContent);
            var otherHasAi = otherNodeTypes.Contains("ai");
            var otherHasGates = otherNodeTypes.Contains("human-gate");
            var otherHasParallel = otherNodeTypes.Contains("parallel") || otherNodeTypes.Contains("foreach");
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
            // Simple name similarity: count shared words
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

    private static IReadOnlyList<string> ExtractNodeTypes(string yaml)
    {
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in NodeTypeRegex().Matches(yaml))
        {
            var t = m.Groups[1].Value.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(t)) types.Add(t);
        }
        return [.. types.Order()];
    }

    private static IReadOnlyList<string> ExtractConnectorIds(string yaml)
    {
        // Extract connector references (url domains or explicit connector: fields)
        var connectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in ConnectorRegex().Matches(yaml))
        {
            var c = m.Groups[1].Value.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(c)) connectors.Add(c);
        }
        return [.. connectors.Order()];
    }

    private static int CountEdges(string yaml)
    {
        // Count "- id:" lines under edges section
        var edgeBlock = false;
        var count = 0;
        foreach (var line in yaml.Split('\n'))
        {
            if (line.TrimStart().StartsWith("edges:")) { edgeBlock = true; continue; }
            if (edgeBlock && line.TrimStart().StartsWith("- id:")) count++;
            if (edgeBlock && !line.StartsWith(' ') && !line.StartsWith('\t') && !line.TrimStart().StartsWith("- id:"))
                edgeBlock = false;
        }
        return count;
    }

    private static string ComputeHash(
        string trigger, IReadOnlyList<string> nodeTypes, IReadOnlyList<string> connectors,
        bool hasAi, bool hasGates, bool hasParallel)
    {
        var input = $"{trigger}|{string.Join(',', nodeTypes)}|{string.Join(',', connectors)}|{hasAi}|{hasGates}|{hasParallel}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..16].ToLowerInvariant();
    }

    [GeneratedRegex(@"^\s+type:\s+([a-zA-Z\-]+)", RegexOptions.Multiline)]
    private static partial Regex NodeTypeRegex();

    [GeneratedRegex(@"connector:\s+([a-zA-Z0-9\-_]+)", RegexOptions.Multiline)]
    private static partial Regex ConnectorRegex();
}
