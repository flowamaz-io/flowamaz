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
        // Plain scalars in node config (timeout_hours: 24, isDefault: true) must reach workers as JSON
        // numbers/booleans, not strings — otherwise every numeric/boolean setting is silently ignored.
        .WithAttemptingUnquotedStringTypeDeserialization()
        .IgnoreUnmatchedProperties()
        .Build();

    // Re-serialises a YAML config subtree to JSON so node config is exposed as a JsonDocument.
    private static readonly ISerializer YamlToJson = new SerializerBuilder()
        .JsonCompatible()
        .DisableAliases()
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
        var edges = ResolveBranchConditions(MapEdges(edgeDtos, nodeIds), edgeDtos, nodes);

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
            meta,
            MapInputs(root.Spec?.Variables ?? root.Variables));
    }

    /// <summary>
    /// Declared inputs. Accepts <c>name: { type: number, required: true }</c> and the shorthand <c>name: number</c>;
    /// anything else is ignored (inputs are informational for the engine).
    /// </summary>
    private static List<WorkflowInput> MapInputs(object? variables)
    {
        var inputs = new List<WorkflowInput>();
        if (variables is not IDictionary<object, object?> declared) return inputs;
        foreach (var (rawName, value) in declared)
        {
            var name = rawName?.ToString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            string? type = null;
            var required = false;
            switch (value)
            {
                case string shorthand:
                    type = shorthand.Trim().ToLowerInvariant();
                    break;
                case IDictionary<object, object?> map:
                    foreach (var (k, v) in map)
                    {
                        var key = k?.ToString();
                        if (string.Equals(key, "type", StringComparison.OrdinalIgnoreCase))
                            type = v?.ToString()?.Trim().ToLowerInvariant();
                        else if (string.Equals(key, "required", StringComparison.OrdinalIgnoreCase))
                            required = v is bool b ? b : string.Equals(v?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
                    }
                    break;
            }
            inputs.Add(new WorkflowInput(name.Trim(), string.IsNullOrEmpty(type) ? null : type, required));
        }
        return inputs;
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
                MapRetry(dto.Id, dto.Retry),
                MapTimeout(dto.Id, dto.Timeout),
                MapCompensation(dto.Id, dto.Compensate ?? dto.Compensation)));
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

    /// <summary>
    /// The canvas stores router branches on the router node (<c>config.branches: [{ label, condition,
    /// isDefault }]</c>) and links each edge to a branch by label (<c>via</c>). Copy the branch condition
    /// onto its edge (an explicit edge condition still wins) and flag default branches, so the
    /// orchestrator evaluates canvas-built routers the same as hand-written YAML.
    /// </summary>
    private static List<SfgEdge> ResolveBranchConditions(List<SfgEdge> edges, List<EdgeDto>? dtos, List<SfgNode> nodes)
    {
        if (dtos is null || dtos.Count != edges.Count) return edges;
        var byId = nodes.ToDictionary(n => n.Id);
        var result = new List<SfgEdge>(edges.Count);
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            var via = dtos[i].Via?.Trim();
            if (!string.IsNullOrEmpty(via)) edge = edge with { Via = via };
            if (string.IsNullOrEmpty(via) || !byId.TryGetValue(edge.FromNodeId, out var source)
                || source.Config.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object
                || !source.Config.RootElement.TryGetProperty("branches", out var branches)
                || branches.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                result.Add(edge);
                continue;
            }

            var branch = branches.EnumerateArray().FirstOrDefault(b =>
                b.ValueKind == System.Text.Json.JsonValueKind.Object && b.TryGetProperty("label", out var l)
                && string.Equals(l.GetString(), via, StringComparison.Ordinal));
            if (branch.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                result.Add(edge);
                continue;
            }

            var isDefault = branch.TryGetProperty("isDefault", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.True;
            var condition = edge.Condition
                ?? (branch.TryGetProperty("condition", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(c.GetString()) ? c.GetString() : null);
            result.Add(edge with { Condition = isDefault ? null : condition, IsDefault = isDefault });
        }
        return result;
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

    // Retry, timeout and compensation blocks. The documented form (docs/yaml-spec.md and the v1 JSON
    // schema) is snake_case: retry.max_attempts / backoff_seconds / backoff_multiplier, timeout.seconds /
    // on_timeout, compensate.strategy / node_id. Earlier engine versions only read camelCase
    // (maxAttempts, timeoutSeconds, onTimeoutNodeId, compensation.compensateNodeId), so a block written
    // the documented way was silently dropped. Both spellings are read; keys are matched ignoring case
    // and underscores.

    private static RetryPolicy? MapRetry(string nodeId, Dictionary<string, object?>? block)
    {
        if (block is null) return null;
        var keys = Normalise(block);
        return new RetryPolicy(
            Int(nodeId, "retry", keys, "maxattempts") ?? 0,
            Int(nodeId, "retry", keys, "backoffseconds") ?? 0,
            Number(nodeId, "retry", keys, "backoffmultiplier") ?? 1.0);
    }

    private static TimeoutPolicy? MapTimeout(string nodeId, Dictionary<string, object?>? block)
    {
        if (block is null) return null;
        var keys = Normalise(block);
        return new TimeoutPolicy(
            Int(nodeId, "timeout", keys, "seconds", "timeoutseconds") ?? 0,
            Text(keys, "ontimeout", "ontimeoutnodeid"));
    }

    private static CompensationBlock? MapCompensation(string nodeId, Dictionary<string, object?>? block)
    {
        if (block is null) return null;
        var keys = Normalise(block);
        var strategy = Text(keys, "strategy");
        return strategy is null ? null : new CompensationBlock(strategy, Text(keys, "nodeid", "compensatenodeid"));
    }

    private static Dictionary<string, object?> Normalise(Dictionary<string, object?> block)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in block)
        {
            result[key.Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant()] = value;
        }
        return result;
    }

    private static object? First(Dictionary<string, object?> keys, string[] names)
    {
        foreach (var name in names)
        {
            if (keys.TryGetValue(name, out var value) && value is not null) return value;
        }
        return null;
    }

    private static string? Text(Dictionary<string, object?> keys, params string[] names)
    {
        var value = First(keys, names)?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int? Int(string nodeId, string block, Dictionary<string, object?> keys, params string[] names)
    {
        var number = Number(nodeId, block, keys, names);
        return number is null ? null : (int)Math.Round(number.Value);
    }

    private static double? Number(string nodeId, string block, Dictionary<string, object?> keys, params string[] names)
    {
        var value = First(keys, names);
        if (value is null) return null;
        var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        throw new SfgParseException(
            $"Node '{nodeId}' has {block}.{Snake(names[0])} = '{text}', which is not a number. Use a number, for example {block}: {{ {Snake(names[0])}: 3 }}.");
    }

    private static string Snake(string normalised) => normalised switch
    {
        "maxattempts" => "max_attempts",
        "backoffseconds" => "backoff_seconds",
        "backoffmultiplier" => "backoff_multiplier",
        _ => normalised,
    };

    private sealed class RootDto
    {
        public WorkflowDto? Workflow { get; set; }
        public MetadataDto? Metadata { get; set; }
        public SpecDto? Spec { get; set; }
        public List<NodeDto>? Nodes { get; set; }
        public List<EdgeDto>? Edges { get; set; }
        /// <summary>Usually a map of name → definition; typed loosely so any shape still parses.</summary>
        public object? Variables { get; set; }
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
        /// <summary>Usually a map of name → definition; typed loosely so any shape still parses.</summary>
        public object? Variables { get; set; }
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
        public Dictionary<string, object?>? Retry { get; set; }
        public Dictionary<string, object?>? Timeout { get; set; }
        /// <summary>The documented key (docs/yaml-spec.md).</summary>
        public Dictionary<string, object?>? Compensate { get; set; }
        /// <summary>The key earlier engine versions read; still accepted.</summary>
        public Dictionary<string, object?>? Compensation { get; set; }
    }

    private sealed class EdgeDto
    {
        public string? Id { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Condition { get; set; }
        /// <summary>Canvas: the label of the router branch this edge belongs to.</summary>
        public string? Via { get; set; }
    }
}
