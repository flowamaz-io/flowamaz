using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Models;

/// <summary>
/// Per-workspace operational settings, persisted as a jsonb column on
/// <see cref="Entities.Workspaces.Workspace"/>. Defaults mirror the Community/Starter tier so a
/// freshly created workspace is immediately usable.
/// </summary>
public class WorkspaceSettings
{
    public int MaxConcurrentRuns { get; set; } = 10;
    public int RunRetentionDays { get; set; } = 7;
    public decimal AiCostBudgetMonthUsd { get; set; }
    public List<string> AllowedAiProviders { get; set; } = [AiProvidersDefault];
    public Dictionary<string, string> DefaultAiModelOverrides { get; set; } = new();
    public MarketplacePolicy MarketplacePolicy { get; set; } = MarketplacePolicy.OfficialAndVerified;

    private const string AiProvidersDefault = "anthropic";
}
