using Flowamaz.Core.Enums;
using YamlDotNet.RepresentationModel;

namespace Flowamaz.Application.Workflow.Scheduling;

/// <summary>Trigger declared by a workflow definition.</summary>
public sealed record WorkflowTriggerInfo(WorkflowTriggerType Type, string? Cron, string? TimeZone);

/// <summary>
/// Reads the trigger declaration from workflow YAML (via YamlDotNet — CLAUDE.md rule 5).
/// Sources (a non-manual declaration in either wins):
///   1. <c>spec.trigger: { type: schedule, config: { cron, timezone } }</c> (flowamaz/v1 spec)
///   2. the Trigger node's config: <c>{ trigger_type|triggerType|type: schedule, cron, timezone }</c> (canvas form)
/// Anything unrecognised is Manual.
/// </summary>
public static class WorkflowTriggerReader
{
    public static WorkflowTriggerInfo Read(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml)) return new(WorkflowTriggerType.Manual, null, null);

        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return new(WorkflowTriggerType.Manual, null, null);
        }
        if (root is null) return new(WorkflowTriggerType.Manual, null, null);

        WorkflowTriggerInfo? fromSpec = null;
        if (Map(root, "spec") is { } spec && Map(spec, "trigger") is { } trigger)
        {
            var config = Map(trigger, "config");
            fromSpec = new(Parse(Scalar(trigger, "type")),
                config is null ? null : Scalar(config, "cron"), config is null ? null : Scalar(config, "timezone"));
        }

        WorkflowTriggerInfo? fromNode = null;
        var nodes = Seq(Map(root, "spec") ?? root, "nodes");
        var triggerNode = nodes?.Children.OfType<YamlMappingNode>()
            .FirstOrDefault(n => string.Equals(Scalar(n, "type"), "trigger", StringComparison.OrdinalIgnoreCase));
        if (triggerNode is not null && Map(triggerNode, "config") is { } cfg)
            fromNode = new(Parse(Scalar(cfg, "trigger_type") ?? Scalar(cfg, "triggerType") ?? Scalar(cfg, "type")),
                Scalar(cfg, "cron"), Scalar(cfg, "timezone"));

        // The canvas skeleton writes spec.trigger: manual while the trigger panel stores the real type on
        // the node, so whichever source declares a non-manual trigger wins.
        if (fromSpec is { Type: not WorkflowTriggerType.Manual }) return fromSpec;
        if (fromNode is { Type: not WorkflowTriggerType.Manual }) return fromNode;
        return fromSpec ?? fromNode ?? new(WorkflowTriggerType.Manual, null, null);
    }

    private static WorkflowTriggerType Parse(string? value) =>
        Enum.TryParse<WorkflowTriggerType>(value?.Replace("-", string.Empty).Replace("_", string.Empty), ignoreCase: true, out var t)
            ? t : WorkflowTriggerType.Manual;

    private static YamlMappingNode? Map(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var v) ? v as YamlMappingNode : null;

    private static YamlSequenceNode? Seq(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var v) ? v as YamlSequenceNode : null;

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s && !string.IsNullOrWhiteSpace(s.Value)
            ? s.Value : null;
}
