namespace Flowamaz.Core.Models;

/// <summary>The resolved AI configuration view for a workspace (GET .../ai-config).</summary>
public sealed record AiConfigView(
    IReadOnlyList<string> AllowedProviders,
    AiBudgetView? Budget,
    IReadOnlyList<AiFunctionResolution> Functions);

/// <summary>How one AI function resolves and from which level of the hierarchy (workspace/org/platform).</summary>
public sealed record AiFunctionResolution(string FunctionId, string ModelId, string Provider, string ResolvedFrom);

/// <summary>Workspace AI budget snapshot.</summary>
public sealed record AiBudgetView(long MonthlyTokenLimit, long TokensUsedThisMonth, DateTime BudgetResetDate, bool IsHardCapped);

/// <summary>One requested per-function override (PUT .../ai-config).</summary>
public sealed record FunctionOverrideInput(string FunctionId, string Provider, string ModelId, string KeySource);
