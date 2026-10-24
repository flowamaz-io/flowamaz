using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private static readonly Regex EmailPattern = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);
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
        if (jsonString is not null)
        {
            try
            {
                var tree = JsonNode.Parse(jsonString);
                CoerceToSchema(tree, await GetSchemaAsync(ct));
                jsonString = tree?.ToJsonString() ?? jsonString;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "[WorkflowValidator] type coercion skipped");
            }
        }

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
            // An array item error ("spec.nodes[3]: ArrayItemNotValid") hides the field that is actually wrong,
            // so report the innermost errors with their full path instead.
            var issues = schema.Validate(jsonString).SelectMany(Innermost);
            foreach (var issue in issues)
            {
                errors.Add(new ValidationIssue(
                    Layer: 1,
                    Code: $"SCH-{(int)issue.Kind:D3}",
                    Message: DescribeSchemaIssue(issue),
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

    private static IEnumerable<ValidationError> Innermost(ValidationError error) =>
        error is ChildSchemaValidationError child && child.Errors.Count > 0
            ? child.Errors.SelectMany(kv => kv.Value).SelectMany(Innermost)
            : [error];

    /// <summary>Plain-language schema error: which field, what is wrong with it, and how to fix it.</summary>
    internal static string DescribeSchemaIssue(ValidationError issue)
    {
        var path = string.IsNullOrEmpty(issue.Path) ? "the document" : issue.Path.TrimStart('#', '/');
        var field = issue.Property is { Length: > 0 } p && !path.EndsWith(p, StringComparison.Ordinal) ? $"{path}.{p}" : path;
        var allowed = issue.Schema?.ActualSchema.Enumeration is { Count: > 0 } values
            ? string.Join(", ", values.Select(v => v?.ToString()))
            : null;
        return issue.Kind switch
        {
            ValidationErrorKind.NoAdditionalPropertiesAllowed =>
                $"'{field}' is not a field the workflow format recognises. Remove it, or move step settings under that step's config.",
            ValidationErrorKind.PropertyRequired => $"'{field}' is required but missing. Add it.",
            ValidationErrorKind.NotInEnumeration =>
                $"'{field}' has a value that isn't allowed." + (allowed is null ? "" : $" Use one of: {allowed}."),
            ValidationErrorKind.IntegerExpected => $"'{field}' must be a whole number (for example 30).",
            ValidationErrorKind.NumberExpected => $"'{field}' must be a number.",
            ValidationErrorKind.BooleanExpected => $"'{field}' must be true or false.",
            ValidationErrorKind.StringExpected => $"'{field}' must be text.",
            ValidationErrorKind.ObjectExpected => $"'{field}' must be a set of named fields (a mapping).",
            ValidationErrorKind.ArrayExpected => $"'{field}' must be a list.",
            ValidationErrorKind.PatternMismatch when issue.Schema?.ActualSchema.Pattern == "^[a-z0-9-]+$" =>
                $"'{field}' must use only lowercase letters, numbers and hyphens (for example manager-approval).",
            ValidationErrorKind.PatternMismatch => $"'{field}' is not in the required format.",
            ValidationErrorKind.StringTooShort => $"'{field}' must not be empty.",
            ValidationErrorKind.TooFewItems => $"'{field}' needs at least one item.",
            _ => $"'{field}': {issue.Kind}. Fix this field per the workflow-v1 specification.",
        };
    }

    /// <summary>
    /// YAML has no types without a schema: the untyped YamlDotNet pass turns every scalar into a string, so
    /// "timeout: { seconds: 30 }" reached the JSON schema as "30" and failed IntegerExpected. The runtime parser
    /// reads those fields as numbers, so convert a string where the schema expects a number or boolean (and
    /// does not also accept text). Free-form config objects have no property schema and are left untouched.
    /// </summary>
    internal static void CoerceToSchema(JsonNode? node, JsonSchema schema)
    {
        var actual = schema.ActualSchema;
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    JsonSchema? child = actual.Properties.TryGetValue(key, out var prop) ? prop : actual.AdditionalPropertiesSchema;
                    if (child is null) continue;
                    if (obj[key] is JsonValue value && TryCoerce(value, child.ActualSchema.Type, out var typed)) obj[key] = typed;
                    else CoerceToSchema(obj[key], child);
                }
                break;
            case JsonArray array when actual.Item is not null:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && TryCoerce(value, actual.Item.ActualSchema.Type, out var typed)) array[i] = typed;
                    else CoerceToSchema(array[i], actual.Item);
                }
                break;
        }
    }

    private static bool TryCoerce(JsonValue value, JsonObjectType type, out JsonNode? typed)
    {
        typed = null;
        if (!value.TryGetValue<string>(out var text) || type.HasFlag(JsonObjectType.String)) return false;
        if (type.HasFlag(JsonObjectType.Integer) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            typed = JsonValue.Create(l);
        else if (type.HasFlag(JsonObjectType.Number) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            typed = JsonValue.Create(d);
        else if (type.HasFlag(JsonObjectType.Boolean) && bool.TryParse(text, out var b))
            typed = JsonValue.Create(b);
        return typed is not null;
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
            return new SerializerBuilder().JsonCompatible().DisableAliases().Build().Serialize(obj);
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
            // The gate worker reads timeout_hours (canvas: timeoutHours); "timeout" is the legacy key.
            if (gate.Timeout == null && (gate.Config == null
                || !(gate.Config.ContainsKey("timeout") || gate.Config.ContainsKey("timeout_hours") || gate.Config.ContainsKey("timeoutHours") || gate.Config.ContainsKey("timeoutValue"))))
                warnings.Add(new ValidationIssue(6, "BPR-001",
                    $"Human gate '{gate.Id}' sets no timeout, so it uses the default of {Flowamaz.Core.Entities.Workflow.GateTimeoutActions.DefaultTimeoutHours} hours " +
                    "and the run fails if nobody decides by then. Set timeout_hours (and on_timeout: escalate, auto-approve or auto-reject) to choose the deadline and what happens.",
                    gate.Id, null, null));
        }

        // BPR-012 — "escalate" with nobody to escalate to: at the deadline the run fails instead (run 4: every
        // AI-built gate was set this way). The gate panel warned; Validate did not.
        foreach (var gate in doc.Spec?.Nodes?.Where(n => n.Type?.Equals("human-gate", StringComparison.OrdinalIgnoreCase) == true) ?? [])
        {
            var cfg = gate.Config;
            var onTimeout = cfg is not null && cfg.TryGetValue("on_timeout", out var ot) && ot.ValueKind == JsonValueKind.String ? ot.GetString()
                : cfg is not null && cfg.TryGetValue("onTimeout", out var ot2) && ot2.ValueKind == JsonValueKind.String ? ot2.GetString()
                : gate.Timeout?.OnTimeout;
            if (Flowamaz.Core.Entities.Workflow.GateTimeoutActions.Normalize(onTimeout) != Flowamaz.Core.Entities.Workflow.GateTimeoutActions.Escalate) continue;
            var target = cfg is null ? null
                : HasText(cfg, "escalate_to") ? cfg["escalate_to"].GetString()
                : HasText(cfg, "escalateTo") ? cfg["escalateTo"].GetString() : null;
            if (target is not null && EmailPattern.IsMatch(target.Trim())) continue;
            var label = string.IsNullOrWhiteSpace(gate.Label) ? gate.Id : gate.Label;
            warnings.Add(new ValidationIssue(6, "BPR-012",
                $"Human gate '{gate.Id}' ({label}) escalates on timeout but has no escalation email, so the run fails at the deadline instead. " +
                "Set escalate_to to the person's email, or change on_timeout to fail, auto-approve or auto-reject.",
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

        // BPR-011 — a decision with a single exit: every case that doesn't match that one path has nowhere to go
        // (for example, removing "Finance approves" from a photo left "Over 1000?" with only its "no" exit).
        var edges = doc.Spec?.Edges ?? [];
        foreach (var router in doc.Spec?.Nodes?.Where(n => n.Type?.Equals("router", StringComparison.OrdinalIgnoreCase) == true) ?? [])
        {
            var exits = edges.Where(e => string.Equals(e.From, router.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exits.Count != 1) continue;
            var only = exits[0];
            var branch = string.IsNullOrWhiteSpace(only.Via) ? "" : $" (\"{only.Via}\")";
            var label = string.IsNullOrWhiteSpace(router.Label) ? router.Id : router.Label;
            warnings.Add(new ValidationIssue(6, "BPR-011",
                $"Decision '{router.Id}' ({label}) has only one exit{branch}, to '{only.To}', so any case that doesn't take it has nowhere to go. " +
                "Connect the missing branch, or remove the decision and connect the steps directly.",
                router.Id, null, null));
        }

        // BPR-010 — runtime-readiness: action/AI nodes without a connector configured
        foreach (var node in doc.Spec?.Nodes ?? [])
        {
            var isAction = node.Type?.Equals("action", StringComparison.OrdinalIgnoreCase) == true;
            var isAi = node.Type?.Equals("ai", StringComparison.OrdinalIgnoreCase) == true;
            if (!isAction && !isAi) continue;

            // Runnable when it names a connector (spec key connector_id, or the canvas key connector)
            // or, for action nodes, a raw HTTP url. AI nodes are configured by prompt/model instead.
            var cfg = node.Config;
            var hasConnector = cfg is not null && (HasText(cfg, "connector_id") || HasText(cfg, "connector"));
            var hasUrl = isAction && cfg is not null && HasText(cfg, "url");
            var aiConfigured = isAi && cfg is not null && (HasText(cfg, "prompt") || hasConnector);
            if (hasConnector || hasUrl || aiConfigured) continue;

            var label = string.IsNullOrWhiteSpace(node.Label) ? node.Id : node.Label;
            warnings.Add(new ValidationIssue(6, "BPR-010",
                $"Action node '{node.Id}' ({label}) has no connector configured. " +
                "It will fail at runtime. Open the node config to select a connector and action.",
                node.Id, null, null));
        }
    }

    private static bool HasText(Dictionary<string, JsonElement> config, string key) =>
        config.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString());

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
        [JsonPropertyName("on_timeout")] public string? OnTimeout { get; set; }
    }

    private sealed class EdgeDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("via")] public string? Via { get; set; }
    }
}
