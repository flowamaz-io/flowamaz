namespace Flowamaz.Core.Models;

/// <summary>A generated narrative for one audience. Content is markdown.</summary>
public sealed record InterpreterNarrative(string Audience, string Content, DateTime GeneratedAt);

/// <summary>Result of one AI completion — text plus the token counts used for metering.</summary>
public sealed record AiCompletionResult(string Text, int TokensInput, int TokensOutput);

/// <summary>Step-debugger snapshot. Variables are UNMASKED — admin/dev-staging only.</summary>
public sealed record DebugSnapshot(
    Guid InstanceId,
    string Status,
    string? CurrentNodeId,
    IReadOnlyList<DebugNode> NodeStates,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<string> RecentEvents,
    IReadOnlyList<string> NextEligibleNodes);

public sealed record DebugNode(string NodeId, string NodeType, string Status, int RetryCount);
