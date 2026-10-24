using Flowamaz.Core.Exceptions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;

namespace Flowamaz.Application.Workflow.Creation;

/// <summary>
/// Method 3 — visual input (FUNCTIONAL.md §6.3). The vision model turns an image into a graph of
/// nodes with confidence scores. Nothing is dropped: elements under <see cref="ConfidenceThreshold"/>
/// are flagged for review. Two paths:
/// <list type="bullet">
/// <item><b>Review</b> — <see cref="PreviewImageAsync"/> returns the graph; the user keeps / renames /
/// removes flagged nodes; <see cref="BuildYamlFromConfirmedGraph"/> turns the confirmed graph into YAML.</item>
/// <item><b>One-shot</b> (API clients that skip review) — <see cref="ProcessImageAsync"/> includes
/// low-confidence nodes with <c>config.review_needed: true</c> and <c>config.review_reason</c>, so the
/// canvas can highlight them and nothing silently disappears.</item>
/// </list>
/// </summary>
public sealed class VisualInputService : IVisualInputService
{
    public const double ConfidenceThreshold = 0.80;

    /// <summary>Node types the workflow-v1 schema accepts. Anything else detected becomes an action.</summary>
    public static readonly IReadOnlySet<string> AllowedNodeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "trigger", "action", "ai", "human-gate", "router", "end", "foreach", "parallel",
        "try-catch", "wait", "sub-workflow", "annotation",
    };

    private static readonly IReadOnlyDictionary<string, string> ConnectorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["sap"] = "sap-s4hana", ["salesforce"] = "salesforce-crm", ["slack"] = "slack",
        ["teams"] = "ms-teams", ["bamboohr"] = "bamboohr", ["servicenow"] = "servicenow",
        ["jira"] = "jira", ["hubspot"] = "hubspot", ["zendesk"] = "zendesk",
        ["github"] = "github", ["gitlab"] = "gitlab", ["aws"] = "aws-s3",
        ["azure"] = "azure-blob", ["google"] = "google-drive", ["stripe"] = "stripe",
        ["twilio"] = "twilio", ["sendgrid"] = "sendgrid", ["datadog"] = "datadog",
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private static readonly ISerializer YamlWriter = new SerializerBuilder().DisableAliases().Build();
    private static readonly Regex InvalidIdChars = new("[^a-z0-9-]+", RegexOptions.Compiled);

    private const string SystemPrompt = """
        You are a workflow diagram parser. Extract all process elements from the provided image.
        Return JSON ONLY. No explanation. For each element include:
        id, label, type (trigger|action|router|human-gate|end|parallel|foreach|wait|ai|sub-workflow|annotation),
        x, y coordinates (approximate), confidence (0-1), annotations (system names, notes), conditions (for routers),
        and reason — one short sentence explaining any confidence below 0.8 (e.g. "handwriting is smudged",
        "shape could be a decision or an action"). Omit reason when confident.
        Also return "title": the diagram's heading if it has one (e.g. "Expense claim process"), else a short
        name for the process; leave out words like "draft".
        Format: {"title":"...","nodes":[...],"edges":[{"from":"id","to":"id","label":null}],"low_confidence":["id",...]}
        """;

    private readonly IAiCompletionService _ai;
    private readonly IModelResolutionService _models;
    private readonly IAiTokenMeteringService _metering;
    private readonly IWorkflowValidator _validator;
    private readonly ILogger<VisualInputService> _log;

    public VisualInputService(
        IAiCompletionService ai,
        IModelResolutionService models,
        IAiTokenMeteringService metering,
        IWorkflowValidator validator,
        ILogger<VisualInputService> log)
    {
        _ai = ai;
        _models = models;
        _metering = metering;
        _validator = validator;
        _log = log;
    }

    public async Task<VisualInputResult> ProcessImageAsync(byte[] imageBytes, string mimeType, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        _log.LogInformation("VisualInputService.ProcessImageAsync entry workspaceId={WorkspaceId} bytes={Bytes}", workspaceId, imageBytes.Length);
        try
        {
            var preview = await PreviewImageAsync(imageBytes, mimeType, workspaceId, cancellationToken);
            if (preview.Nodes.Count == 0)
            {
                _log.LogInformation("VisualInputService.ProcessImageAsync exit no nodes detected");
                return new VisualInputResult(string.Empty, [], preview.CostUsd, preview.TokensUsed);
            }

            var lowConfidence = preview.Nodes
                .Where(n => n.ReviewNeeded)
                .Select(n => new LowConfidenceElement(n.Id, n.Label, n.Type, n.Confidence, n.Annotations, n.Reason))
                .ToList();

            var wfId = $"visual-workflow-{workspaceId.ToString()[..8]}";
            var yaml = BuildYaml(
                preview.Nodes.Select(n => new BuildNode(n.Id, n.Label, n.Type, n.SuggestedConnectors,
                    n.ReviewNeeded, n.ReviewNeeded ? n.Reason : null)).ToList(),
                preview.Edges, wfId, preview.SuggestedName ?? "Visual Workflow");

            _log.LogInformation("VisualInputService.ProcessImageAsync exit nodes={N} lowConf={L}", preview.Nodes.Count, lowConfidence.Count);
            return new VisualInputResult(yaml, lowConfidence, preview.CostUsd, preview.TokensUsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "VisualInputService.ProcessImageAsync error workspaceId={WorkspaceId}", workspaceId);
            throw;
        }
    }

    public async Task<VisualPreviewResult> PreviewImageAsync(byte[] imageBytes, string mimeType, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        _log.LogInformation("VisualInputService.PreviewImageAsync entry workspaceId={WorkspaceId} bytes={Bytes}", workspaceId, imageBytes.Length);
        try
        {
            var imageBase64 = Convert.ToBase64String(imageBytes);
            var modelConfig = await _models.ResolveModelConfigAsync(AiFunctionIds.VisualInput, workspaceId, cancellationToken);
            AiNotConfiguredException.ThrowIfNoKey(modelConfig, "Reading a diagram or whiteboard photo");

            var result = await _ai.CompleteWithImageAsync(
                modelConfig, SystemPrompt, imageBase64, mimeType,
                "Parse this workflow diagram. Return only the JSON structure.", cancellationToken);

            var cost = EstimateCost(result.TokensInput, result.TokensOutput);
            _metering.RecordUsage(AiFunctionIds.VisualInput, modelConfig.ModelId, modelConfig.Provider,
                null, workspaceId, result.TokensInput, result.TokensOutput, cost);

            var tokens = result.TokensInput + result.TokensOutput;
            var parse = ParseVisionResponse(result.Text);
            if (parse is null)
            {
                _log.LogWarning("VisualInputService.PreviewImageAsync malformed JSON from AI");
                return new VisualPreviewResult([], [], ConfidenceThreshold, cost, tokens);
            }

            var preview = ToPreview(parse, cost, tokens);
            _log.LogInformation("VisualInputService.PreviewImageAsync exit nodes={N} reviewNeeded={R} edges={E}",
                preview.Nodes.Count, preview.Nodes.Count(n => n.ReviewNeeded), preview.Edges.Count);
            return preview;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "VisualInputService.PreviewImageAsync error workspaceId={WorkspaceId}", workspaceId);
            throw;
        }
    }

    public string BuildYamlFromConfirmedGraph(ConfirmedVisualGraph graph, string workflowId, string workflowName)
    {
        _log.LogInformation("VisualInputService.BuildYamlFromConfirmedGraph entry nodes={N} edges={E}", graph.Nodes.Count, graph.Edges.Count);
        try
        {
            // Sanitise ids the same way the preview did (idempotent for ids that came from the preview)
            // so API clients that post their own graph still produce schema-valid ids.
            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var nodes = new List<BuildNode>();
            foreach (var n in graph.Nodes)
            {
                var id = UniqueId(SanitiseId(n.Id), used);
                idMap.TryAdd(n.Id, id);
                nodes.Add(new BuildNode(id, n.Label.Trim(), NormaliseType(n.Type),
                    n.SuggestedConnectors ?? [], false, null));
            }

            var edges = graph.Edges
                .Select(e => new DetectedEdge(
                    idMap.TryGetValue(e.From, out var f) ? f : e.From,
                    idMap.TryGetValue(e.To, out var t) ? t : e.To,
                    e.Label))
                .ToList();

            var bridged = BridgeRemovedNodes(edges, nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal));
            var yaml = BuildYaml(nodes, bridged, workflowId, workflowName);

            _log.LogInformation("VisualInputService.BuildYamlFromConfirmedGraph exit nodes={N} edges={E}", nodes.Count, bridged.Count);
            return yaml;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "VisualInputService.BuildYamlFromConfirmedGraph error");
            throw;
        }
    }

    /// <summary>
    /// When the user removes a node, its incoming and outgoing edges are reconnected
    /// (predecessor → successor) so the remaining graph stays reachable from the trigger.
    /// Edges that point at ids that are not kept are treated the same way.
    /// </summary>
    internal static List<DetectedEdge> BridgeRemovedNodes(IReadOnlyList<DetectedEdge> edges, IReadOnlySet<string> keptIds)
    {
        var work = edges.Where(e => !string.IsNullOrWhiteSpace(e.From) && !string.IsNullOrWhiteSpace(e.To)).ToList();
        while (true)
        {
            var removed = work.SelectMany(e => new[] { e.From, e.To }).FirstOrDefault(id => !keptIds.Contains(id));
            if (removed is null) break;

            var incoming = work.Where(e => e.To == removed && e.From != removed).ToList();
            var outgoing = work.Where(e => e.From == removed && e.To != removed).ToList();
            work.RemoveAll(e => e.From == removed || e.To == removed);

            // The incoming edge's label wins: it is the predecessor's branch ("yes" out of "Over 1000?"), and
            // the bridged edge still leaves that predecessor.
            foreach (var inEdge in incoming)
                foreach (var outEdge in outgoing)
                    if (inEdge.From != outEdge.To)
                        work.Add(new DetectedEdge(inEdge.From, outEdge.To, inEdge.Label ?? outEdge.Label));
        }

        // De-duplicate on the branch label too. Removing "Finance approves" bridges "Over 1000? —yes→ Pay
        // employee", which has the same ends as the existing "—no→" edge; keying on (from, to) alone dropped
        // the "yes" branch and left the decision with one exit.
        return work
            .GroupBy(e => (e.From, e.To, e.Label))
            .Select(g => g.First())
            .ToList();
    }

    private VisualPreviewResult ToPreview(VisualParseResult parse, decimal cost, int tokens)
    {
        var flaggedByAi = parse.LowConfidence.ToHashSet(StringComparer.Ordinal);
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new List<DetectedNode>();
        var index = 0;

        foreach (var raw in parse.Nodes)
        {
            index++;
            var id = UniqueId(SanitiseId(string.IsNullOrWhiteSpace(raw.Id) ? $"node-{index}" : raw.Id), used);
            if (!string.IsNullOrEmpty(raw.Id)) idMap.TryAdd(raw.Id, id);

            var confidence = Math.Clamp(raw.ConfidenceScore ?? raw.Confidence ?? 1.0, 0.0, 1.0);
            var typeKnown = AllowedNodeTypes.Contains(raw.Type ?? string.Empty);
            var reviewNeeded = confidence < ConfidenceThreshold || flaggedByAi.Contains(raw.Id) || !typeKnown;
            var label = string.IsNullOrWhiteSpace(raw.Label) ? $"Step {index}" : raw.Label.Trim();

            nodes.Add(new DetectedNode(
                id, label, NormaliseType(raw.Type), confidence, reviewNeeded,
                reviewNeeded ? DescribeReason(raw, confidence, typeKnown) : null,
                raw.Annotations, MatchConnectors(raw.Annotations.Append(label)), raw.X, raw.Y));
        }

        var edges = parse.Edges
            .Where(e => idMap.ContainsKey(e.From) && idMap.ContainsKey(e.To))
            .Select(e => new DetectedEdge(idMap[e.From], idMap[e.To], string.IsNullOrWhiteSpace(e.Label) ? null : e.Label.Trim()))
            .ToList();

        var title = string.IsNullOrWhiteSpace(parse.Title) ? null : parse.Title.Trim();
        if (title is { Length: > 120 }) title = title[..120];
        return new VisualPreviewResult(nodes, edges, ConfidenceThreshold, cost, tokens, title);
    }

    private static string DescribeReason(ParsedNode raw, double confidence, bool typeKnown)
    {
        if (!string.IsNullOrWhiteSpace(raw.Reason)) return raw.Reason.Trim();
        if (!typeKnown)
            return $"The shape type '{raw.Type}' is not a workflow step type, so it was read as an action. Check what this step does.";
        return $"Detected with {confidence:P0} confidence — the label or shape was hard to read. Confirm the name or remove it.";
    }

    private sealed record BuildNode(
        string Id, string Label, string Type, IReadOnlyList<string> Connectors,
        bool ReviewNeeded, string? ReviewReason);

    /// <summary>
    /// Serialises a graph to workflow-v1 YAML through YamlDotNet. Adds a manual trigger / end node when the
    /// diagram had none, wired to the entry / exit nodes, so a sketch without explicit start/end shapes
    /// still produces a runnable definition. Validation is the caller's job.
    /// </summary>
    private static string BuildYaml(IReadOnlyList<BuildNode> nodes, IReadOnlyList<DetectedEdge> edges, string workflowId, string workflowName)
    {
        var nodeList = nodes.ToList();
        var edgeList = edges.ToList();
        var ids = nodeList.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

        if (nodeList.Count > 0 && !nodeList.Any(n => n.Type == "trigger"))
        {
            var startId = UniqueId("start", ids);
            var entries = nodeList.Where(n => n.Type != "annotation" && !edgeList.Any(e => e.To == n.Id)).ToList();
            nodeList.Insert(0, new BuildNode(startId, "Start", "trigger", [], false, null));
            edgeList.InsertRange(0, entries.Select(n => new DetectedEdge(startId, n.Id)));
        }

        if (nodeList.Count > 0 && !nodeList.Any(n => n.Type == "end"))
        {
            var endId = UniqueId("end", ids);
            var exits = nodeList.Where(n => n.Type != "annotation" && !edgeList.Any(e => e.From == n.Id)).ToList();
            nodeList.Add(new BuildNode(endId, "End", "end", [], false, null));
            edgeList.AddRange(exits.Select(n => new DetectedEdge(n.Id, endId)));
        }

        var yamlNodes = nodeList.Select(n =>
        {
            var config = new Dictionary<string, object>();
            if (n.Connectors.Count > 0) config["suggested_connectors"] = n.Connectors.ToList();
            if (n.ReviewNeeded)
            {
                config["review_needed"] = true;
                if (!string.IsNullOrWhiteSpace(n.ReviewReason)) config["review_reason"] = n.ReviewReason;
            }

            // Diagram coordinates are not written: the validator's YAML→JSON pass turns untyped scalars into
            // strings, which fails the schema's numeric position fields. The canvas auto-lays out instead.
            return new Dictionary<string, object>
            {
                ["id"] = n.Id,
                ["type"] = n.Type,
                ["label"] = n.Label,
                ["config"] = config,
            };
        }).ToList();

        var edgeIdx = 1;
        var yamlEdges = edgeList.Select(e =>
        {
            var edge = new Dictionary<string, object> { ["id"] = $"e{edgeIdx++}", ["from"] = e.From, ["to"] = e.To };
            if (!string.IsNullOrWhiteSpace(e.Label)) edge["via"] = e.Label;
            return edge;
        }).ToList();

        var doc = new Dictionary<string, object>
        {
            ["apiVersion"] = "flowamaz/v1",
            ["kind"] = "Workflow",
            ["metadata"] = new Dictionary<string, object>
            {
                ["id"] = SanitiseId(workflowId),
                ["name"] = string.IsNullOrWhiteSpace(workflowName) ? "Visual Workflow" : workflowName,
                ["description"] = "Generated from uploaded diagram",
                ["version"] = "1.0.0",
            },
            ["spec"] = new Dictionary<string, object>
            {
                ["trigger"] = new Dictionary<string, object> { ["type"] = "manual" },
                ["nodes"] = yamlNodes,
                ["edges"] = yamlEdges,
            },
        };

        return YamlWriter.Serialize(doc);
    }

    internal static string SanitiseId(string raw)
    {
        var id = InvalidIdChars.Replace((raw ?? string.Empty).Trim().ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrEmpty(id) ? "node" : id;
    }

    private static string UniqueId(string id, ISet<string> used)
    {
        var candidate = id;
        var n = 2;
        while (!used.Add(candidate)) candidate = $"{id}-{n++}";
        return candidate;
    }

    internal static string NormaliseType(string? type)
    {
        var t = (type ?? string.Empty).Trim().ToLowerInvariant();
        return AllowedNodeTypes.Contains(t) ? t : "action";
    }

    private VisualParseResult? ParseVisionResponse(string json)
    {
        try
        {
            // Strip markdown fences if present
            var clean = json.Trim();
            if (clean.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) clean = clean[7..].Trim();
            else if (clean.StartsWith("```", StringComparison.Ordinal)) clean = clean[3..].Trim();
            if (clean.EndsWith("```", StringComparison.Ordinal)) clean = clean[..^3].Trim();

            return JsonSerializer.Deserialize<VisualParseResult>(clean, JsonOpts);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "VisualInputService.ParseVisionResponse failed to parse JSON");
            return null;
        }
    }

    private static List<string> MatchConnectors(IEnumerable<string> annotations)
    {
        var matched = new List<string>();
        foreach (var annotation in annotations)
        {
            foreach (var (key, connectorId) in ConnectorMap)
            {
                if (Regex.IsMatch(annotation, $@"\b{Regex.Escape(key)}\b", RegexOptions.IgnoreCase) && !matched.Contains(connectorId))
                    matched.Add(connectorId);
            }
        }
        return matched;
    }

    private static decimal EstimateCost(int tokensIn, int tokensOut) => (tokensIn + tokensOut) * 0.000003m;

    // DTOs for parsing Claude's JSON response
    private sealed class VisualParseResult
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("nodes")] public List<ParsedNode> Nodes { get; set; } = [];
        [JsonPropertyName("edges")] public List<ParsedEdge> Edges { get; set; } = [];
        [JsonPropertyName("low_confidence")] public List<string> LowConfidence { get; set; } = [];
    }

    private sealed class ParsedNode
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("type")] public string? Type { get; set; } = "action";
        [JsonPropertyName("x")] public double? X { get; set; }
        [JsonPropertyName("y")] public double? Y { get; set; }
        [JsonPropertyName("confidence")] public double? Confidence { get; set; }
        [JsonPropertyName("confidence_score")] public double? ConfidenceScore { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("annotations")] public List<string> Annotations { get; set; } = [];
        [JsonPropertyName("conditions")] public List<string> Conditions { get; set; } = [];
    }

    private sealed class ParsedEdge
    {
        [JsonPropertyName("from")] public string From { get; set; } = "";
        [JsonPropertyName("to")] public string To { get; set; } = "";
        [JsonPropertyName("label")] public string? Label { get; set; }
    }
}
