using System.Text.Json;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Flowamaz.Core.Workflow;

/// <summary>
/// Parses FlowAmaz workflow YAML into a validated <see cref="WorkflowGraph"/> using YamlDotNet
/// (never raw string manipulation — CLAUDE.md §5). Structural rules are enforced up front so the
/// orchestrator only ever walks a sound graph: exactly one Trigger, ≥1 End, every edge endpoint
/// exists, and no node is unreachable from the Trigger.
/// </summary>
public sealed class SfgParser
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    // Re-serialises a YAML config subtree to JSON so node config is exposed as a JsonDocument.
    private static readonly ISerializer YamlToJson = new SerializerBuilder()
        .JsonCompatible()
        .Build();

    public Task<WorkflowGraph> ParseAsync(string yamlContent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Parse(yamlContent));
    }

    public WorkflowGraph Parse(string yamlContent)
    {
        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            throw new SfgParseException("Workflow YAML is empty. A workflow needs a 'nodes' list with one Trigger and at least one End node.");
        }

        RootDto root;
        try
        {
            root = YamlDeserializer.Deserialize<RootDto>(yamlContent)
                   ?? throw new SfgParseException("Workflow YAML deserialised to nothing. Check the document structure.");
        }
        catch (YamlException ex)
        {
            throw new SfgParseException(
                $"Workflow YAML is malformed at line {ex.Start.Line}, column {ex.Start.Column}: {ex.Message}. Fix the YAML syntax and retry.",
                ex);
        }

        // Templates/exports use the documented flowamaz/v1 envelope (metadata: + spec.nodes/spec.edges);
        // hand-authored workflows declare workflow:/nodes:/edges: at the root. Read whichever is present
        // so both formats parse identically (prompt fix-07-01 — runtime aligned with the v1 schema).
        var nodeDtos = root.Spec?.Nodes ?? root.Nodes;
        var edgeDtos = root.Spec?.Edges ?? root.Edges;

        if (nodeDtos is null || nodeDtos.Count == 0)
        {
            throw new SfgParseException("Workflow has no nodes. Add at least a Trigger node and one End node.");
        }

        var nodes = MapNodes(nodeDtos);
        var nodeIds = nodes.Select(n => n.Id).ToHashSet();
        var edges = MapEdges(edgeDtos, nodeIds);

        ValidateExactlyOneTrigger(nodes);
        ValidateHasEnd(nodes);
        ValidateNoOrphans(nodes, edges);

        var meta = new WorkflowMetadata(
            root.Workflow?.Name ?? root.Metadata?.Name ?? "Untitled workflow",
            root.Workflow?.Description ?? root.Metadata?.Description);

        return new WorkflowGraph(
            root.Workflow?.Id ?? root.Metadata?.Id ?? "unknown",
            root.Workflow?.Version ?? root.Metadata?.Version ?? "draft",
            nodes,
            edges,
            meta);
    }

    private static List<SfgNode> MapNodes(List<NodeDto> dtos)
    {
        var nodes = new List<SfgNode>(dtos.Count);
        var seen = new HashSet<string>();

        foreach (var dto in dtos)
        {
            if (string.IsNullOrWhiteSpace(dto.Id))
            {
                throw new SfgParseException("A node is missing its 'id'. Every node needs a unique id.");
            }

            if (!seen.Add(dto.Id))
            {
                throw new SfgParseException($"Duplicate node id '{dto.Id}'. Node ids must be unique within a workflow.");
            }

            // The v1 schema uses hyphenated type names (e.g. "human-gate", "if-else"); strip separators
            // so they map to the NodeType enum members (HumanGate, IfElse) the same as PascalCase input.
            var normalizedType = dto.Type?.Replace("-", string.Empty).Replace("_", string.Empty);
            if (!Enum.TryParse<NodeType>(normalizedType, ignoreCase: true, out var type))
            {
                throw new SfgParseException(
                    $"Node '{dto.Id}' has unknown type '{dto.Type}'. Valid types: {string.Join(", ", Enum.GetNames<NodeType>())}.");
            }

            nodes.Add(new SfgNode(
                dto.Id,
                type,
                dto.Label ?? dto.Id,
                ConfigToJson(dto.Config),
                MapRetry(dto.Retry),
                MapTimeout(dto.Timeout),
                MapCompensation(dto.Compensation)));
        }

        return nodes;
    }

    private static List<SfgEdge> MapEdges(List<EdgeDto>? dtos, HashSet<string> nodeIds)
    {
        if (dtos is null) return [];

        var edges = new List<SfgEdge>(dtos.Count);
        var index = 0;
        foreach (var dto in dtos)
        {
            var id = string.IsNullOrWhiteSpace(dto.Id) ? $"edge-{index}" : dto.Id;

            if (string.IsNullOrWhiteSpace(dto.From) || !nodeIds.Contains(dto.From))
            {
                throw new SfgParseException(
                    $"Edge '{id}' references a 'from' node '{dto.From}' that does not exist. Point it at a defined node id.");
            }

            if (string.IsNullOrWhiteSpace(dto.To) || !nodeIds.Contains(dto.To))
            {
                throw new SfgParseException(
                    $"Edge '{id}' references a 'to' node '{dto.To}' that does not exist. Point it at a defined node id.");
            }

            var condition = string.IsNullOrWhiteSpace(dto.Condition) ? null : dto.Condition;
            edges.Add(new SfgEdge(id, dto.From, dto.To, condition));
            index++;
        }

        return edges;
    }

    private static void ValidateExactlyOneTrigger(List<SfgNode> nodes)
    {
        var triggers = nodes.Count(n => n.Type == NodeType.Trigger);
        if (triggers != 1)
        {
            throw new SfgParseException(
                $"A workflow must have exactly one Trigger node; found {triggers}. Add or remove Trigger nodes so there is exactly one entry point.");
        }
    }

    private static void ValidateHasEnd(List<SfgNode> nodes)
    {
        if (!nodes.Any(n => n.Type == NodeType.End))
        {
            throw new SfgParseException("A workflow must have at least one End node so execution can terminate. Add an End node.");
        }
    }

    private static void ValidateNoOrphans(List<SfgNode> nodes, List<SfgEdge> edges)
    {
        var trigger = nodes.First(n => n.Type == NodeType.Trigger);
        var adjacency = edges.GroupBy(e => e.FromNodeId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ToNodeId).ToList());

        var reachable = new HashSet<string> { trigger.Id };
        var queue = new Queue<string>();
        queue.Enqueue(trigger.Id);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adjacency.TryGetValue(current, out var next)) continue;
            foreach (var to in next)
            {
                if (reachable.Add(to)) queue.Enqueue(to);
            }
        }

        var orphan = nodes.FirstOrDefault(n => n.Type != NodeType.Trigger && !reachable.Contains(n.Id));
        if (orphan is not null)
        {
            throw new SfgParseException(
                $"Node '{orphan.Id}' is orphaned — it is not reachable from the Trigger. Add an edge into it or remove it.");
        }
    }

    private static JsonDocument ConfigToJson(object? config)
    {
        if (config is null) return JsonDocument.Parse("{}");
        var json = YamlToJson.Serialize(config);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
    }

    private static RetryPolicy? MapRetry(RetryDto? d) =>
        d is null ? null : new RetryPolicy(d.MaxAttempts, d.BackoffSeconds, d.BackoffMultiplier);

    private static TimeoutPolicy? MapTimeout(TimeoutDto? d) =>
        d is null ? null : new TimeoutPolicy(d.TimeoutSeconds, d.OnTimeoutNodeId);

    private static CompensationBlock? MapCompensation(CompensationDto? d) =>
        d?.Strategy is null ? null : new CompensationBlock(d.Strategy, d.CompensateNodeId);

    private sealed class RootDto
    {
        public WorkflowDto? Workflow { get; set; }
        public MetadataDto? Metadata { get; set; }
        public SpecDto? Spec { get; set; }
        public List<NodeDto>? Nodes { get; set; }
        public List<EdgeDto>? Edges { get; set; }
    }

    // flowamaz/v1 envelope: metadata: header + spec: body.
    private sealed class MetadataDto
    {
        public string? Id { get; set; }
        public string? Version { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
    }

    private sealed class SpecDto
    {
        public List<NodeDto>? Nodes { get; set; }
        public List<EdgeDto>? Edges { get; set; }
    }

    private sealed class WorkflowDto
    {
        public string? Id { get; set; }
        public string? Version { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
    }

    private sealed class NodeDto
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? Label { get; set; }
        public object? Config { get; set; }
        public RetryDto? Retry { get; set; }
        public TimeoutDto? Timeout { get; set; }
        public CompensationDto? Compensation { get; set; }
    }

    private sealed class EdgeDto
    {
        public string? Id { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Condition { get; set; }
    }

    private sealed class RetryDto
    {
        public int MaxAttempts { get; set; }
        public int BackoffSeconds { get; set; }
        public double BackoffMultiplier { get; set; } = 1.0;
    }

    private sealed class TimeoutDto
    {
        public int TimeoutSeconds { get; set; }
        public string? OnTimeoutNodeId { get; set; }
    }

    private sealed class CompensationDto
    {
        public string? Strategy { get; set; }
        public string? CompensateNodeId { get; set; }
    }
}
