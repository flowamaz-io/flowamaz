using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace Flowamaz.Application.Workflow.Creation;

/// <summary>
/// Deterministic clean-up of AI-generated workflow YAML before validation. Models reliably get the shape
/// right but slip on details the schema is strict about (a trigger type of "event", "${var}" references,
/// extra keys on a node, "decision" instead of "router"). Fixing those here is free; asking the model to
/// fix them costs a second call and does not always work. Only unambiguous fixes are made — anything
/// structural (missing branches, unreachable steps) is left for the validator to report.
/// </summary>
internal static class GeneratedYamlRepair
{
    private static readonly IDeserializer Reader = new DeserializerBuilder().Build();
    private static readonly ISerializer Writer = new SerializerBuilder().DisableAliases().Build();

    private static readonly HashSet<string> TopKeys = ["apiVersion", "kind", "metadata", "spec"];
    private static readonly HashSet<string> MetadataKeys = ["id", "name", "description", "version", "workspace"];
    private static readonly HashSet<string> SpecKeys = ["trigger", "default_model", "sla_threshold_ms", "variables", "nodes", "edges", "groups"];
    private static readonly HashSet<string> NodeKeys = ["id", "type", "label", "config", "content", "position", "retry", "timeout", "compensate"];
    private static readonly HashSet<string> EdgeKeys = ["id", "from", "to", "via", "condition"];
    private static readonly HashSet<string> TriggerKeys = ["type", "config"];
    private static readonly HashSet<string> TriggerTypes = ["webhook", "form", "schedule", "manual", "sub-workflow"];
    private static readonly HashSet<string> NodeTypes =
        ["action", "ai", "human-gate", "router", "end", "foreach", "parallel", "try-catch", "wait", "sub-workflow", "annotation", "trigger"];

    private static readonly Regex DollarVariable = new(@"\$\{\s*([A-Za-z_][\w.]*)\s*\}", RegexOptions.Compiled);
    private static readonly Regex NotIdChar = new("[^a-z0-9-]+", RegexOptions.Compiled);
    private static readonly Regex SemVer = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);

    /// <summary>Returns the repaired YAML, or the input unchanged when it can't be read as a mapping.</summary>
    public static string Repair(string yaml)
    {
        Dictionary<object, object?>? doc;
        try { doc = Reader.Deserialize<Dictionary<object, object?>>(yaml); }
        catch { return yaml; }
        if (doc is null || !(doc.ContainsKey("spec") || doc.ContainsKey("apiVersion"))) return yaml;

        Prune(doc, TopKeys);
        doc["apiVersion"] = "flowamaz/v1";
        doc["kind"] = "Workflow";

        if (doc.TryGetValue("metadata", out var m) && m is Dictionary<object, object?> metadata)
        {
            Prune(metadata, MetadataKeys);
            if (metadata.TryGetValue("id", out var id) && id is string s) metadata["id"] = Slug(s);
            if (metadata.TryGetValue("version", out var v) && (v is not string ver || !SemVer.IsMatch(ver))) metadata["version"] = "1.0.0";
        }

        if (doc.TryGetValue("spec", out var sp) && sp is Dictionary<object, object?> spec)
        {
            Prune(spec, SpecKeys);
            if (spec.TryGetValue("trigger", out var t))
                spec["trigger"] = RepairTrigger(t);

            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            if (spec.TryGetValue("nodes", out var n) && n is List<object?> nodes)
                foreach (var node in nodes.OfType<Dictionary<object, object?>>())
                    RepairNode(node, idMap);

            if (spec.TryGetValue("edges", out var e) && e is List<object?> edges)
            {
                var index = 0;
                foreach (var edge in edges.OfType<Dictionary<object, object?>>())
                {
                    index++;
                    if (!edge.ContainsKey("via") && edge.TryGetValue("label", out var label) && label is string l && l.Length > 0)
                        edge["via"] = l;
                    Prune(edge, EdgeKeys);
                    if (!edge.TryGetValue("id", out var eid) || eid is not string { Length: > 0 }) edge["id"] = $"e{index}";
                    foreach (var end in new[] { "from", "to" })
                        if (edge.TryGetValue(end, out var target) && target is string ts && idMap.TryGetValue(ts, out var mapped))
                            edge[end] = mapped;
                }
            }
        }

        // Nothing needed fixing: keep the model's own formatting (comments, flow style) instead of re-emitting it.
        var repaired = Writer.Serialize(ReplaceDollarVariables(doc));
        return repaired == Writer.Serialize(Reader.Deserialize<object>(yaml)) ? yaml : repaired;
    }

    private static object RepairTrigger(object? trigger)
    {
        var dict = trigger as Dictionary<object, object?> ?? new Dictionary<object, object?> { ["type"] = trigger?.ToString() ?? "manual" };
        Prune(dict, TriggerKeys);
        var raw = (dict.TryGetValue("type", out var t) ? t?.ToString() : null)?.Trim().ToLowerInvariant().Replace('_', '-') ?? "manual";
        dict["type"] = TriggerTypes.Contains(raw) ? raw : raw switch
        {
            "event" or "api" or "http" or "webhook-event" or "system" or "integration" => "webhook",
            "form-submission" or "submission" or "request" or "email" or "user" or "human" => "form",
            "cron" or "scheduled" or "timer" or "recurring" or "time" => "schedule",
            "subworkflow" or "workflow" => "sub-workflow",
            _ => "manual",
        };
        return dict;
    }

    private static void RepairNode(Dictionary<object, object?> node, Dictionary<string, string> idMap)
    {
        if (node.TryGetValue("id", out var id) && id is string original)
        {
            var slug = Slug(original);
            if (slug != original) idMap[original] = slug;
            node["id"] = slug;
        }

        var type = (node.TryGetValue("type", out var t) ? t?.ToString() : null)?.Trim().ToLowerInvariant().Replace('_', '-') ?? "action";
        node["type"] = NodeTypes.Contains(type) ? type : type switch
        {
            "start" or "begin" => "trigger",
            "stop" or "finish" or "complete" or "done" => "end",
            "approval" or "gate" or "humangate" or "human" or "review" or "manual-approval" => "human-gate",
            "decision" or "condition" or "if" or "switch" or "branch" or "gateway" => "router",
            "note" or "comment" => "annotation",
            "delay" or "timer" or "wait-until" => "wait",
            "llm" or "ai-step" or "ai-node" => "ai",
            _ => "action",
        };

        if (!node.TryGetValue("label", out var label) || label is not string { Length: > 0 })
            node["label"] = node.TryGetValue("id", out var nid) && nid is string sid ? Humanise(sid) : "Step";

        // Extra keys ("description", "assignee", "connector"…) belong in the free-form config.
        var extras = node.Keys.Select(k => k.ToString()!).Where(k => !NodeKeys.Contains(k)).ToList();
        if (extras.Count > 0)
        {
            var config = node.TryGetValue("config", out var c) && c is Dictionary<object, object?> existing ? existing : new Dictionary<object, object?>();
            foreach (var key in extras)
            {
                config.TryAdd(key, node[key]);
                node.Remove(key);
            }
            node["config"] = config;
        }
        if (node.TryGetValue("config", out var cfg) && cfg is not Dictionary<object, object?>) node["config"] = new Dictionary<object, object?>();
        if (!node["type"]!.Equals("annotation")) node.Remove("content");
    }

    private static object? ReplaceDollarVariables(object? value) => value switch
    {
        string s => DollarVariable.Replace(s, "{{ $1 }}"),
        Dictionary<object, object?> d => d.ToDictionary(kv => kv.Key, kv => ReplaceDollarVariables(kv.Value)),
        List<object?> l => l.Select(ReplaceDollarVariables).ToList(),
        _ => value,
    };

    private static void Prune(Dictionary<object, object?> dict, IReadOnlySet<string> allowed)
    {
        foreach (var key in dict.Keys.Where(k => !allowed.Contains(k.ToString()!)).ToList()) dict.Remove(key);
    }

    private static string Slug(string raw)
    {
        var slug = NotIdChar.Replace(raw.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-'), "-").Trim('-');
        return slug.Length == 0 ? "step" : slug;
    }

    private static string Humanise(string id) =>
        id.Length == 0 ? "Step" : char.ToUpperInvariant(id[0]) + id[1..].Replace('-', ' ');
}
