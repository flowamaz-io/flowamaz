using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Creation;

public sealed class VisualInputService : IVisualInputService
{
    private const double ConfidenceThreshold = 0.80;
    private const int MaxLowConfirmations = 5;

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
    };

    private const string SystemPrompt = """
        You are a workflow diagram parser. Extract all process elements from the provided image.
        Return JSON ONLY. No explanation. For each element include:
        id, label, type (trigger|action|router|human-gate|end|parallel|foreach|wait|ai|sub-workflow|annotation),
        x, y coordinates (approximate), confidence_score (0-1), annotations (system names, notes), conditions (for routers).
        Format: {"nodes":[...],"edges":[{"from":"id","to":"id","label":null}],"low_confidence":["id",...]}
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

        var imageBase64 = Convert.ToBase64String(imageBytes);
        var modelConfig = await _models.ResolveModelConfigAsync(AiFunctionIds.VisualInput, workspaceId, cancellationToken);

        var result = await _ai.CompleteWithImageAsync(
            modelConfig, SystemPrompt, imageBase64, mimeType,
            "Parse this workflow diagram. Return only the JSON structure.", cancellationToken);

        _metering.RecordUsage(AiFunctionIds.VisualInput, modelConfig.ModelId, modelConfig.Provider,
            null, workspaceId, result.TokensInput, result.TokensOutput, EstimateCost(result.TokensInput, result.TokensOutput));

        var parse = ParseVisionResponse(result.Text);
        if (parse is null)
        {
            _log.LogWarning("VisualInputService.ProcessImageAsync malformed JSON from AI");
            return new VisualInputResult(string.Empty, [], EstimateCost(result.TokensInput, result.TokensOutput), result.TokensInput + result.TokensOutput);
        }

        var lowConfidenceNodes = parse.Nodes
            .Where(n => n.Confidence < ConfidenceThreshold)
            .Take(MaxLowConfirmations)
            .Select(n => new LowConfidenceElement(n.Id, n.Label, n.Type, n.Confidence, n.Annotations))
            .ToList();

        var highConfidenceNodes = parse.Nodes.Where(n => n.Confidence >= ConfidenceThreshold).ToList();

        var yaml = BuildYamlFromParsedNodes(highConfidenceNodes, parse.Edges, workspaceId);
        var cost = EstimateCost(result.TokensInput, result.TokensOutput);

        _log.LogInformation("VisualInputService.ProcessImageAsync exit nodes={N} lowConf={L}", highConfidenceNodes.Count, lowConfidenceNodes.Count);
        return new VisualInputResult(yaml, lowConfidenceNodes, cost, result.TokensInput + result.TokensOutput);
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

    private string BuildYamlFromParsedNodes(IReadOnlyList<ParsedNode> nodes, IReadOnlyList<ParsedEdge> edges, Guid workspaceId)
    {
        var wfId = $"visual-workflow-{workspaceId.ToString()[..8]}";
        var sb = new StringBuilder();
        sb.AppendLine("apiVersion: flowamaz/v1");
        sb.AppendLine("kind: Workflow");
        sb.AppendLine("metadata:");
        sb.AppendLine($"  id: {wfId}");
        sb.AppendLine("  name: \"Visual Workflow\"");
        sb.AppendLine("  description: \"Generated from uploaded diagram\"");
        sb.AppendLine("  version: \"1.0.0\"");
        sb.AppendLine("spec:");
        sb.AppendLine("  trigger:");
        sb.AppendLine("    type: manual");
        sb.AppendLine("  nodes:");

        foreach (var node in nodes)
        {
            var connectors = MatchConnectors(node.Annotations);
            sb.AppendLine($"    - id: {node.Id}");
            sb.AppendLine($"      type: {node.Type}");
            sb.AppendLine($"      label: \"{node.Label}\"");
            if (connectors.Count > 0)
                sb.AppendLine($"      # connectors: {string.Join(", ", connectors)}");
            sb.AppendLine("      config: {}");
        }

        sb.AppendLine("  edges:");
        var edgeIdx = 1;
        foreach (var edge in edges)
        {
            sb.AppendLine($"    - id: e{edgeIdx++}");
            sb.AppendLine($"      from: {edge.From}");
            sb.AppendLine($"      to: {edge.To}");
            if (!string.IsNullOrEmpty(edge.Label))
                sb.AppendLine($"      via: \"{edge.Label}\"");
        }

        return sb.ToString();
    }

    private static List<string> MatchConnectors(IReadOnlyList<string> annotations)
    {
        var matched = new List<string>();
        foreach (var annotation in annotations)
        {
            foreach (var (key, connectorId) in ConnectorMap)
            {
                if (annotation.Contains(key, StringComparison.OrdinalIgnoreCase) && !matched.Contains(connectorId))
                    matched.Add(connectorId);
            }
        }
        return matched;
    }

    private static decimal EstimateCost(int tokensIn, int tokensOut) => (tokensIn + tokensOut) * 0.000003m;

    // DTOs for parsing Claude's JSON response
    private sealed class VisualParseResult
    {
        [JsonPropertyName("nodes")] public List<ParsedNode> Nodes { get; set; } = [];
        [JsonPropertyName("edges")] public List<ParsedEdge> Edges { get; set; } = [];
        [JsonPropertyName("low_confidence")] public List<string> LowConfidence { get; set; } = [];
    }

    private sealed class ParsedNode
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "action";
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("confidence")] public double Confidence { get; set; } = 1.0;
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
