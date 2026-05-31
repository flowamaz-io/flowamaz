using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging;
using NJsonSchema;
using NJsonSchema.Validation;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Flowamaz.Application.Workflow.Validation;

/// <summary>
/// Six-layer workflow YAML validator. All layers run regardless of Layer 1 result so
/// all issues are reported in a single pass.
/// </summary>
public sealed class WorkflowValidator : IWorkflowValidator
{
    // System variables always available in {{ }} expressions
    private static readonly HashSet<string> SystemVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "workflow.id", "workflow.name", "instance.id", "node.id", "now"
    };

    // Secret patterns (Layer 5)
    private static readonly Regex[] SecretPatterns =
    [
        new Regex(@"\bsk-[A-Za-z0-9]{20,}", RegexOptions.Compiled),
        new Regex(@"\bghp_[A-Za-z0-9]{36}", RegexOptions.Compiled),
        new Regex(@"\bxoxb-[0-9]+-[A-Za-z0-9]+", RegexOptions.Compiled),
        new Regex(@"(?i)\bpassword\s*[:=]\s*\S{8,}", RegexOptions.Compiled),
        new Regex(@"(?i)\bapi.?key\s*[:=]\s*[A-Za-z0-9+/_.]{10,}", RegexOptions.Compiled),
        new Regex(@"(?i)\btoken\s*[:=]\s*[A-Za-z0-9+/]{20,}", RegexOptions.Compiled),
        new Regex(@"\bAKIA[0-9A-Z]{16}", RegexOptions.Compiled),
    ];

    private static readonly Regex ExpressionPattern = new(@"\{\{\s*([^}]+?)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex ExternalUrlPattern = new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    private static JsonSchema? _schema;
    private static readonly SemaphoreSlim SchemaSemaphore = new(1, 1);

    private readonly ILogger<WorkflowValidator> _log;

    public WorkflowValidator(ILogger<WorkflowValidator> log)
    {
        _log = log;
    }

    public async Task<ValidationResult> ValidateAsync(string yamlContent, Guid? workspaceId = null, CancellationToken ct = default)
    {
        _log.LogDebug("[WorkflowValidator] Entry workspaceId={WorkspaceId}", workspaceId);

        var errors = new List<ValidationIssue>();
        var warnings = new List<ValidationIssue>();
        var info = new List<ValidationIssue>();

        // Convert YAML → JSON string (NullNamingConvention preserves YAML keys as-is)
        var jsonString = ConvertYamlToJson(yamlContent);

        // Layer 1 — JSON Schema validation
        await RunLayer1Async(jsonString, errors, ct);

        // Parse JSON to DTOs for structural analysis
        WorkflowDocDto? doc = null;
        if (jsonString != null)
        {
            try { doc = JsonSerializer.Deserialize<WorkflowDocDto>(jsonString, JsonOptions); }
            catch { /* parse error already in Layer 1 */ }
        }

        if (doc?.Spec != null)
        {
            var nodeIds = doc.Spec.Nodes?.Select(n => n.Id ?? "").Where(id => id.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            var declaredVars = doc.Spec.Variables?.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

            RunLayer2(doc, nodeIds, errors);
            RunLayer3(jsonString ?? yamlContent, declaredVars, warnings);
            if (workspaceId.HasValue) RunLayer4(doc, info);
            RunLayer5(yamlContent, doc, errors);
            RunLayer6(doc, warnings, info);
        }

        _log.LogDebug("[WorkflowValidator] Exit errors={E} warnings={W} info={I}",
            errors.Count, warnings.Count, info.Count);

        return ValidationResult.WithIssues(errors, warnings, info);
    }

    // ─── Layer 1: JSON Schema ─────────────────────────────────────────────────

    private async Task RunLayer1Async(string? jsonString, List<ValidationIssue> errors, CancellationToken ct)
    {
        if (jsonString == null)
        {
            errors.Add(new ValidationIssue(1, "SCH-000",
                "Workflow YAML could not be parsed. Check the document structure.", null, null, null));
            return;
        }

        try
        {
            var schema = await GetSchemaAsync(ct);
            var issues = schema.Validate(jsonString);
            foreach (var issue in issues)
            {
                errors.Add(new ValidationIssue(
                    Layer: 1,
                    Code: $"SCH-{(int)issue.Kind:D3}",
                    Message: $"{issue.Path}: {issue.Kind} — fix this field per the workflow-v1 specification",
                    NodeId: null,
                    Line: issue.HasLineInfo ? issue.LineNumber : null,
                    Column: issue.HasLineInfo ? issue.LinePosition : null));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "[WorkflowValidator] Layer 1 schema load error");
            errors.Add(new ValidationIssue(1, "SCH-000", $"Schema validation unavailable: {ex.Message}", null, null, null));
        }
    }

    private static async Task<JsonSchema> GetSchemaAsync(CancellationToken ct)
    {
        if (_schema != null) return _schema;
        await SchemaSemaphore.WaitAsync(ct);
        try
        {
            if (_schema != null) return _schema;
            var coreAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Flowamaz.Core")
                ?? Assembly.Load("Flowamaz.Core");

            var resourceName = coreAsm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("workflow-v1.schema.json", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("workflow-v1.schema.json not found as embedded resource.");

            await using var stream = coreAsm.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            var schemaJson = await reader.ReadToEndAsync(ct);
            _schema = await JsonSchema.FromJsonAsync(schemaJson, ct);
            return _schema;
        }
        finally
        {
            SchemaSemaphore.Release();
        }
    }

    private static string? ConvertYamlToJson(string yaml)
    {
        try
        {
            // NullNamingConvention preserves YAML keys exactly (no case transformation)
            var obj = new DeserializerBuilder()
                .WithNamingConvention(NullNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build()
                .Deserialize(yaml);

            if (obj == null) return null;
            return new SerializerBuilder().JsonCompatible().Build().Serialize(obj);
        }
        catch
        {
            return null;
        }
    }

    // ─── Layer 2: Graph Structure ────────────────────────────────────────────

    private static void RunLayer2(WorkflowDocDto doc, HashSet<string> nodeIds, List<ValidationIssue> errors)
    {
        var nodes = doc.Spec?.Nodes ?? [];
        var edges = doc.Spec?.Edges ?? [];

        var triggerNodes = nodes.Where(n => n.Type?.Equals("trigger", StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (triggerNodes.Count == 0)
            errors.Add(new ValidationIssue(2, "GRF-001", "Workflow must have exactly one trigger node. Add a node with type: trigger.", null, null, null));
        else if (triggerNodes.Count > 1)
            errors.Add(new ValidationIssue(2, "GRF-001", $"Workflow has {triggerNodes.Count} trigger nodes; exactly one is allowed.", null, null, null));

        if (!nodes.Any(n => n.Type?.Equals("end", StringComparison.OrdinalIgnoreCase) == true))
            errors.Add(new ValidationIssue(2, "GRF-004", "Workflow must have at least one end node so execution can terminate.", null, null, null));

        foreach (var edge in edges)
        {
            if (!string.IsNullOrWhiteSpace(edge.From) && !nodeIds.Contains(edge.From!))
                errors.Add(new ValidationIssue(2, "GRF-002",
                    $"Edge '{edge.Id}' references non-existent node '{edge.From}'. Define this node or fix the edge.", null, null, null));

            if (!string.IsNullOrWhiteSpace(edge.To) && !nodeIds.Contains(edge.To!))
                errors.Add(new ValidationIssue(2, "GRF-002",
                    $"Edge '{edge.Id}' references non-existent node '{edge.To}'. Define this node or fix the edge.", null, null, null));
        }

        // Orphan detection
        if (triggerNodes.Count == 1)
        {
            var triggerId = triggerNodes[0].Id!;
            var adjacency = edges.GroupBy(e => e.From ?? "")
                .ToDictionary(g => g.Key, g => g.Select(e => e.To ?? "").ToList());

            var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { triggerId };
            var queue = new Queue<string>();
            queue.Enqueue(triggerId);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!adjacency.TryGetValue(cur, out var nexts)) continue;
                foreach (var to in nexts)
                    if (!string.IsNullOrEmpty(to) && reachable.Add(to)) queue.Enqueue(to);
            }

            foreach (var n in nodes.Where(n => n.Id != triggerId && !string.IsNullOrEmpty(n.Id) && !reachable.Contains(n.Id!)))
                errors.Add(new ValidationIssue(2, "GRF-003",
                    $"Node '{n.Id}' is unreachable from the trigger. Connect it or remove it.", n.Id, null, null));
        }
    }

    // ─── Layer 3: Expression Validation ──────────────────────────────────────

    private static void RunLayer3(string content, HashSet<string> declaredVars, List<ValidationIssue> warnings)
    {
        try
        {
            foreach (Match m in ExpressionPattern.Matches(content))
            {
                var expr = m.Groups[1].Value.Trim();
                if (expr.StartsWith("env.", StringComparison.OrdinalIgnoreCase)) continue;
                if (SystemVariables.Contains(expr)) continue;

                var varName = expr.StartsWith("variables.", StringComparison.OrdinalIgnoreCase)
                    ? expr["variables.".Length..]
                    : expr;

                if (!declaredVars.Contains(varName))
                    warnings.Add(new ValidationIssue(3, "EXP-001",
                        $"Expression '{{{{ {expr} }}}}' references variable '{varName}' which is not declared. It must be provided at runtime.",
                        null, null, null));
            }
        }
        catch { /* non-critical */ }
    }

    // ─── Layer 4: Capability Validation ──────────────────────────────────────

    private static void RunLayer4(WorkflowDocDto doc, List<ValidationIssue> info)
    {
        foreach (var node in doc.Spec?.Nodes?.Where(n => n.Type?.Equals("ai", StringComparison.OrdinalIgnoreCase) == true) ?? [])
        {
            info.Add(new ValidationIssue(4, "CAP-001",
                $"AI node '{node.Id}' will use the workspace/org default model. Set config.model to override.",
                node.Id, null, null));
        }
    }

    // ─── Layer 5: Security ───────────────────────────────────────────────────

    private static void RunLayer5(string yaml, WorkflowDocDto doc, List<ValidationIssue> errors)
    {
        foreach (var pattern in SecretPatterns)
        {
            if (pattern.IsMatch(yaml))
            {
                errors.Add(new ValidationIssue(5, "SEC-001",
                    "Potential hardcoded secret detected. Use credential references ({{ credentials.name }}) instead of inline values.",
                    null, null, null));
                break;
            }
        }

        foreach (var node in doc.Spec?.Nodes?.Where(n => n.Type?.Equals("annotation", StringComparison.OrdinalIgnoreCase) == true) ?? [])
        {
            if (node.Content != null && ExternalUrlPattern.IsMatch(node.Content))
            {
                errors.Add(new ValidationIssue(5, "SEC-002",
                    $"Annotation node '{node.Id}' contains an external URL which may be an XSS vector. Remove URLs from annotation content.",
                    node.Id, null, null));
            }
        }
    }

    // ─── Layer 6: Best Practice ───────────────────────────────────────────────

    private static void RunLayer6(WorkflowDocDto doc, List<ValidationIssue> warnings, List<ValidationIssue> info)
    {
        foreach (var gate in doc.Spec?.Nodes?.Where(n => n.Type?.Equals("human-gate", StringComparison.OrdinalIgnoreCase) == true) ?? [])
        {
            if (gate.Timeout == null && (gate.Config == null || !gate.Config.ContainsKey("timeout")))
                warnings.Add(new ValidationIssue(6, "BPR-001",
                    $"Human gate '{gate.Id}' has no timeout. Without a timeout the gate waits forever and may block the workflow.",
                    gate.Id, null, null));
        }

        foreach (var node in doc.Spec?.Nodes?.Where(n => n.Retry?.MaxAttempts > 10) ?? [])
        {
            warnings.Add(new ValidationIssue(6, "BPR-002",
                $"Node '{node.Id}' has retry.max_attempts={node.Retry!.MaxAttempts}. Values above 10 can cause long delays.",
                node.Id, null, null));
        }

        if (string.IsNullOrWhiteSpace(doc.Metadata?.Description))
            warnings.Add(new ValidationIssue(6, "BPR-003",
                "Workflow has no description. Add metadata.description to improve discoverability.",
                null, null, null));

        if (doc.Spec?.SlaThresholdMs == null)
            info.Add(new ValidationIssue(6, "BPR-004",
                "No SLA threshold configured. Set spec.sla_threshold_ms to enable SLA monitoring.",
                null, null, null));

        // BPR-010 — runtime-readiness: action/AI nodes without a connector configured
        foreach (var node in doc.Spec?.Nodes ?? [])
        {
            var isAction = node.Type?.Equals("action", StringComparison.OrdinalIgnoreCase) == true;
            var isAi = node.Type?.Equals("ai", StringComparison.OrdinalIgnoreCase) == true;
            if (!isAction && !isAi) continue;

            var hasConfig = node.Config is { Count: > 0 };
            var hasConnector = node.Config?.ContainsKey("connector_id") == true;
            if (hasConfig && hasConnector) continue;

            var label = string.IsNullOrWhiteSpace(node.Label) ? node.Id : node.Label;
            warnings.Add(new ValidationIssue(6, "BPR-010",
                $"Action node '{node.Id}' ({label}) has no connector configured. " +
                "It will fail at runtime. Open the node config to select a connector and action.",
                node.Id, null, null));
        }
    }

    // ─── DTOs (System.Text.Json deserialization from JSON) ──────────────────

    private sealed class WorkflowDocDto
    {
        [JsonPropertyName("apiVersion")] public string? ApiVersion { get; set; }
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("metadata")] public MetadataDto? Metadata { get; set; }
        [JsonPropertyName("spec")] public SpecDto? Spec { get; set; }
    }

    private sealed class MetadataDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

    private sealed class SpecDto
    {
        [JsonPropertyName("trigger")] public TriggerDto? Trigger { get; set; }
        [JsonPropertyName("sla_threshold_ms")] public long? SlaThresholdMs { get; set; }
        [JsonPropertyName("variables")] public Dictionary<string, object?>? Variables { get; set; }
        [JsonPropertyName("nodes")] public List<NodeDto>? Nodes { get; set; }
        [JsonPropertyName("edges")] public List<EdgeDto>? Edges { get; set; }
    }

    private sealed class TriggerDto
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
    }

    private sealed class NodeDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("label")] public string? Label { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("retry")] public RetryDto? Retry { get; set; }
        [JsonPropertyName("timeout")] public TimeoutDto? Timeout { get; set; }
        [JsonPropertyName("config")] public Dictionary<string, JsonElement>? Config { get; set; }
    }

    private sealed class RetryDto
    {
        [JsonPropertyName("max_attempts")] public int MaxAttempts { get; set; }
    }

    private sealed class TimeoutDto
    {
        [JsonPropertyName("seconds")] public int Seconds { get; set; }
    }

    private sealed class EdgeDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("via")] public string? Via { get; set; }
    }
}
