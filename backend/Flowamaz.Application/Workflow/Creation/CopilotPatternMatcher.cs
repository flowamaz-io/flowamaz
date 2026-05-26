using System.Text.RegularExpressions;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Creation;

public sealed partial class CopilotPatternMatcher : ICopilotPatternMatcher
{
    private readonly ILogger<CopilotPatternMatcher> _log;

    public CopilotPatternMatcher(ILogger<CopilotPatternMatcher> log) => _log = log;

    public PatternMatchResult? TryMatch(string command, string? yamlContent = null)
    {
        _log.LogInformation("CopilotPatternMatcher.TryMatch entry command={Command}", command);

        foreach (var pattern in Patterns)
        {
            var match = pattern.Regex.Match(command);
            if (!match.Success) continue;

            var result = pattern.Build(match, command, yamlContent);
            if (result is null) continue;

            _log.LogInformation("CopilotPatternMatcher.TryMatch exit matched={Pattern}", pattern.Name);
            return result;
        }

        _log.LogInformation("CopilotPatternMatcher.TryMatch exit no_match");
        return null;
    }

    // Build(match, fullCommand, yamlContent) — fullCommand lets builders extract values from the whole input
    private sealed record Pattern(string Name, Regex Regex, Func<Match, string, string?, PatternMatchResult?> Build);

    private static readonly IReadOnlyList<Pattern> Patterns =
    [
        new("add-timeout",
            AddTimeoutRegex(),
            (_, cmd, _) =>
            {
                // Search the full command for number+unit (handles both orderings)
                var numUnitMatch = NumberWithUnitRegex().Match(cmd);
                if (!numUnitMatch.Success) return null;
                var value = int.Parse(numUnitMatch.Groups[1].Value);
                var unit = numUnitMatch.Groups[2].Value.ToLowerInvariant();
                var seconds = unit is "h" or "hour" or "hours" ? value * 3600
                    : unit is "m" or "min" or "minute" or "minutes" ? value * 60
                    : value;
                var nodeMatch = ToOnNodeRegex().Match(cmd);
                var nodeId = nodeMatch.Success ? nodeMatch.Groups[1].Value.Trim() : "target-node";
                return new PatternMatchResult("add-timeout",
                    $"nodes[id={nodeId}].timeout.seconds: {seconds}",
                    $"Add {seconds}s timeout to {nodeId}");
            }),

        new("add-retry",
            AddRetryRegex(),
            (m, _, _) =>
            {
                var times = int.Parse(m.Groups[1].Value);
                var nodeId = m.Groups[2].Success && m.Groups[2].Length > 0 ? m.Groups[2].Value.Trim() : "target-node";
                return new PatternMatchResult("add-retry",
                    $"nodes[id={nodeId}].retry.max_attempts: {times}",
                    $"Set retry max_attempts={times} on {nodeId}");
            }),

        new("connect-nodes",
            ConnectNodesRegex(),
            (m, _, _) =>
            {
                var from = m.Groups[1].Value.Trim();
                var to = m.Groups[2].Value.Trim();
                var id = $"e-{from}-{to}";
                return new PatternMatchResult("connect-nodes",
                    $"edges:\n  - id: {id}\n    from: {from}\n    to: {to}",
                    $"Add edge from {from} to {to}");
            }),

        new("add-human-gate",
            AddHumanGateRegex(),
            (m, _, _) =>
            {
                var afterNode = m.Groups[2].Length > 0 ? m.Groups[2].Value.Trim() : "previous-node";
                var gateLabel = m.Groups[1].Value.Trim();
                var gateId = $"{gateLabel.ToLowerInvariant().Replace(' ', '-')}-gate";
                return new PatternMatchResult("add-human-gate",
                    $"nodes:\n  - id: {gateId}\n    type: human-gate\n    label: \"{gateLabel}\"\n    config: {{}}\n    timeout:\n      seconds: 172800\n" +
                    $"edges:\n  - id: e-{afterNode}-{gateId}\n    from: {afterNode}\n    to: {gateId}",
                    $"Add human-gate '{gateLabel}' after {afterNode}");
            }),

        new("set-sla",
            SetSlaRegex(),
            (m, _, _) =>
            {
                var value = int.Parse(m.Groups[1].Value);
                var unit = m.Groups[2].Value.ToLowerInvariant();
                var ms = unit is "d" or "day" or "days" ? value * 86400000L
                    : unit is "h" or "hour" or "hours" ? value * 3600000L
                    : value * 60000L;
                return new PatternMatchResult("set-sla",
                    $"spec.sla_threshold_ms: {ms}",
                    $"Set SLA threshold to {ms}ms");
            }),

        new("add-error-handler",
            AddErrorHandlerRegex(),
            (m, _, _) =>
            {
                var nodeId = m.Groups[1].Length > 0 ? m.Groups[1].Value.Trim() : "step";
                var handlerId = $"error-handler-{nodeId}";
                var notifyId = $"notify-ops-{nodeId}";
                return new PatternMatchResult("add-error-handler",
                    $"nodes:\n  - id: {handlerId}\n    type: try-catch\n    label: \"Error Handler\"\n    config: {{}}\n" +
                    $"  - id: {notifyId}\n    type: action\n    label: \"Notify Ops\"\n    config: {{}}\n" +
                    $"edges:\n  - id: e-{handlerId}-{notifyId}\n    from: {handlerId}\n    to: {notifyId}",
                    $"Add error handler with ops notification");
            }),

        new("delete-node",
            DeleteNodeRegex(),
            (m, _, _) =>
            {
                var nodeId = m.Groups[1].Value.Trim();
                return new PatternMatchResult("delete-node",
                    $"nodes[id={nodeId}]: DELETE",
                    $"Delete node {nodeId}");
            }),

        new("rename-node",
            RenameNodeRegex(),
            (m, _, _) =>
            {
                var nodeId = m.Groups[1].Value.Trim();
                var newLabel = m.Groups[2].Value.Trim().Trim('"', '\'');
                return new PatternMatchResult("rename-node",
                    $"nodes[id={nodeId}].label: \"{newLabel}\"",
                    $"Rename node {nodeId} to '{newLabel}'");
            }),

        new("add-annotation",
            AddAnnotationRegex(),
            (m, _, _) =>
            {
                var text = m.Groups[1].Length > 0 ? m.Groups[1].Value.Trim().Trim('"', '\'') : "Add annotation text here";
                var id = $"note-{Guid.NewGuid().ToString()[..8]}";
                return new PatternMatchResult("add-annotation",
                    $"nodes:\n  - id: {id}\n    type: annotation\n    label: \"Note\"\n    content: \"{text}\"\n    config: {{}}",
                    $"Add annotation: {text}");
            }),

        new("set-variable",
            SetVariableRegex(),
            (m, _, _) =>
            {
                var varName = m.Groups[1].Value.Trim();
                var rawType = m.Groups[2].Length > 0 ? m.Groups[2].Value.ToLowerInvariant() : "string";
                var varType = rawType is "number" or "integer" ? "number"
                    : rawType is "bool" or "boolean" ? "boolean"
                    : "string";
                return new PatternMatchResult("set-variable",
                    $"spec.variables.{varName}:\n  type: {varType}\n  required: false\n  sensitive: false",
                    $"Add variable '{varName}' of type {varType}");
            }),

        new("add-parallel",
            AddParallelRegex(),
            (_, _, _) =>
            {
                var id = $"parallel-{Guid.NewGuid().ToString()[..8]}";
                return new PatternMatchResult("add-parallel",
                    $"nodes:\n  - id: {id}\n    type: parallel\n    label: \"Parallel Steps\"\n    config: {{}}",
                    "Add parallel execution block");
            }),

        new("add-loop",
            AddLoopRegex(),
            (m, _, _) =>
            {
                var id = $"loop-{Guid.NewGuid().ToString()[..8]}";
                var varName = m.Groups[1].Length > 0 ? m.Groups[1].Value.Trim() : "items";
                return new PatternMatchResult("add-loop",
                    $"nodes:\n  - id: {id}\n    type: foreach\n    label: \"Loop over {varName}\"\n    config:\n      collection: \"${{{varName}}}\"",
                    $"Add foreach loop over {varName}");
            }),

        new("add-ai-node",
            AddAiNodeRegex(),
            (m, _, _) =>
            {
                var label = m.Groups[1].Length > 0 ? m.Groups[1].Value.Trim() : "AI Step";
                var id = label.ToLowerInvariant().Replace(' ', '-');
                return new PatternMatchResult("add-ai-node",
                    $"nodes:\n  - id: {id}\n    type: ai\n    label: \"{label}\"\n    config:\n      model_function: node-exec\n      prompt: \"\"",
                    $"Add AI node '{label}'");
            }),

        new("add-action-node",
            AddActionNodeRegex(),
            (m, _, _) =>
            {
                var label = m.Groups[1].Value.Trim().Trim('"', '\'');
                var id = label.ToLowerInvariant().Replace(' ', '-');
                return new PatternMatchResult("add-action-node",
                    $"nodes:\n  - id: {id}\n    type: action\n    label: \"{label}\"\n    config: {{}}",
                    $"Add action node '{label}'");
            }),

        new("set-compensation",
            SetCompensationRegex(),
            (m, cmd, _) =>
            {
                var nodeId = m.Groups[1].Length > 0 ? m.Groups[1].Value.Trim() : "target-node";
                var strategy = cmd.Contains("forward", StringComparison.OrdinalIgnoreCase) ? "forward" : "backward";
                return new PatternMatchResult("set-compensation",
                    $"nodes[id={nodeId}].compensate.strategy: {strategy}",
                    $"Set compensation strategy {strategy} on {nodeId}");
            }),

        new("add-condition",
            AddConditionRegex(),
            (m, _, _) =>
            {
                var condition = m.Groups[1].Value.Trim();
                var id = $"router-{Guid.NewGuid().ToString()[..8]}";
                return new PatternMatchResult("add-condition",
                    $"nodes:\n  - id: {id}\n    type: router\n    label: \"Condition\"\n    config: {{}}\n" +
                    $"edges:\n  - id: e-{id}-true\n    from: {id}\n    to: true-branch\n    via: \"{condition}\"",
                    $"Add conditional branch: {condition}");
            }),

        new("add-wait",
            AddWaitRegex(),
            (m, _, _) =>
            {
                var value = int.Parse(m.Groups[1].Value);
                var unit = m.Groups[2].Value.ToLowerInvariant();
                var seconds = unit is "d" or "day" or "days" ? value * 86400
                    : unit is "h" or "hour" or "hours" ? value * 3600
                    : value * 60;
                var id = $"wait-{seconds}s";
                return new PatternMatchResult("add-wait",
                    $"nodes:\n  - id: {id}\n    type: wait\n    label: \"Wait {value}{unit[0]}\"\n    config:\n      duration_seconds: {seconds}",
                    $"Add {seconds}s wait step");
            }),

        new("add-trigger",
            AddTriggerRegex(),
            (m, _, _) =>
            {
                var triggerType = m.Groups[1].Value.Trim().ToLowerInvariant() switch
                {
                    var t when t.Contains("webhook") => "webhook",
                    var t when t.Contains("schedule") || t.Contains("cron") || t.Contains("timer") => "schedule",
                    var t when t.Contains("event") => "event",
                    _ => "manual"
                };
                return new PatternMatchResult("add-trigger",
                    $"spec.trigger.type: {triggerType}",
                    $"Set trigger type to {triggerType}");
            }),

        new("add-sub-workflow",
            AddSubWorkflowRegex(),
            (m, _, _) =>
            {
                var name = m.Groups[1].Value.Trim().Trim('"', '\'');
                var id = name.ToLowerInvariant().Replace(' ', '-');
                return new PatternMatchResult("add-sub-workflow",
                    $"nodes:\n  - id: sub-{id}\n    type: sub-workflow\n    label: \"{name}\"\n    config:\n      workflow_id: \"{id}\"",
                    $"Add sub-workflow '{name}'");
            }),

        new("add-sensitive-variable",
            AddSensitiveVarRegex(),
            (m, _, _) =>
            {
                var varName = m.Groups[1].Value.Trim();
                return new PatternMatchResult("add-sensitive-variable",
                    $"spec.variables.{varName}:\n  type: string\n  required: true\n  sensitive: true",
                    $"Add sensitive variable '{varName}'");
            }),
    ];

    [GeneratedRegex(@"(?:add|set).*?timeout|(?:add|set).*?\d+\s*(?:h|hour|m|min|s|sec|second|minute).*?timeout", RegexOptions.IgnoreCase)]
    private static partial Regex AddTimeoutRegex();

    [GeneratedRegex(@"(\d+)\s*(h|hour|hours|m|min|minute|minutes|s|sec|second|seconds)", RegexOptions.IgnoreCase)]
    private static partial Regex NumberWithUnitRegex();

    [GeneratedRegex(@"(?:to|on)\s+(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex ToOnNodeRegex();

    [GeneratedRegex(@"retry.*?(\d+)\s*times?(?:.*?(?:the|for|on)\s+(\S+))?", RegexOptions.IgnoreCase)]
    private static partial Regex AddRetryRegex();

    [GeneratedRegex(@"connect\s+(\S+)\s+to\s+(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectNodesRegex();

    [GeneratedRegex(@"add\s+(manager\s+approval|approval|gate|review|sign.?off)(?:\s+after\s+(\S+))?", RegexOptions.IgnoreCase)]
    private static partial Regex AddHumanGateRegex();

    [GeneratedRegex(@"sla.*?(\d+)\s*(h|hour|hours|d|day|days|m|min|minutes?)", RegexOptions.IgnoreCase)]
    private static partial Regex SetSlaRegex();

    [GeneratedRegex(@"add\s+(?:an?\s+)?(?:error|failure)\s+handler(?:\s+(?:for|on|to)\s+(\S+))?", RegexOptions.IgnoreCase)]
    private static partial Regex AddErrorHandlerRegex();

    [GeneratedRegex(@"(?:delete|remove)\s+(?:node\s+)?(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex DeleteNodeRegex();

    [GeneratedRegex(@"rename\s+(\S+)\s+to\s+(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex RenameNodeRegex();

    [GeneratedRegex(@"add\s+(?:an?\s+)?(?:note|annotation|comment)(?:\s+[""']?(.+?)[""']?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex AddAnnotationRegex();

    [GeneratedRegex(@"(?:add|set|create)\s+variable\s+(\w+)(?:\s+as\s+(\w+))?", RegexOptions.IgnoreCase)]
    private static partial Regex SetVariableRegex();

    [GeneratedRegex(@"(?:run|execute|add)\s+(?:steps?\s+)?in\s+parallel|add\s+parallel\s+(?:steps?|block)", RegexOptions.IgnoreCase)]
    private static partial Regex AddParallelRegex();

    [GeneratedRegex(@"(?:add\s+)?(?:loop|foreach|repeat|iterate)\s+(?:over\s+)?(\w+)?", RegexOptions.IgnoreCase)]
    private static partial Regex AddLoopRegex();

    [GeneratedRegex(@"add\s+(?:an?\s+)?ai\s+(?:step|node|task)(?:\s+(?:called|named|for)\s+(.+))?", RegexOptions.IgnoreCase)]
    private static partial Regex AddAiNodeRegex();

    [GeneratedRegex(@"add\s+(?:an?\s+)?(?:action|step|task)\s+[""']?(.+?)[""']?$", RegexOptions.IgnoreCase)]
    private static partial Regex AddActionNodeRegex();

    [GeneratedRegex(@"(?:set\s+)?(?:compensation|rollback|undo)(?:\s+(?:on|for)\s+(\S+))?(?:\s+(forward|backward|pivot))?", RegexOptions.IgnoreCase)]
    private static partial Regex SetCompensationRegex();

    [GeneratedRegex(@"(?:add\s+)?(?:condition|branch|if)\s+(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex AddConditionRegex();

    [GeneratedRegex(@"(?:add\s+)?wait\s+(?:for\s+)?(\d+)\s*(h|hour|hours|m|min|minutes?|d|day|days)", RegexOptions.IgnoreCase)]
    private static partial Regex AddWaitRegex();

    [GeneratedRegex(@"(?:set\s+)?trigger\s+(?:on|type|to)?\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex AddTriggerRegex();

    [GeneratedRegex(@"add\s+(?:a\s+)?sub.?workflow\s+[""']?(.+?)[""']?$", RegexOptions.IgnoreCase)]
    private static partial Regex AddSubWorkflowRegex();

    [GeneratedRegex(@"add\s+(?:sensitive|secret|secure)\s+variable\s+(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex AddSensitiveVarRegex();
}
