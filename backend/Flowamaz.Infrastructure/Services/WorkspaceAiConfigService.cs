using System.Text.Json;
using System.Text.Json.Nodes;
using Flowamaz.Application.Ai;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Entities.Ai;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Reads/updates per-workspace AI model overrides (FUNCTIONAL.md §5.3). The read view shows the
/// resolved model and level per function without resolving API keys (so a missing platform key
/// never breaks the view). Updates validate each override against the org provider allowlist and
/// the model capability gates before persisting.
/// </summary>
public sealed class WorkspaceAiConfigService : IWorkspaceAiConfigService
{
    private readonly FlowAmazDbContext _db;
    private readonly ILogger<WorkspaceAiConfigService> _logger;

    public WorkspaceAiConfigService(FlowAmazDbContext db, ILogger<WorkspaceAiConfigService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AiConfigView> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WorkspaceAiConfigService.GetAsync enter workspaceId={WorkspaceId}", workspaceId);
        try
        {
            var workspace = await _db.Workspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken)
                ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");

            var allowedProviders = await ResolveAllowedProvidersAsync(workspace.OrgId, cancellationToken);

            var budgetRow = await _db.WorkspaceAiBudgets.AsNoTracking().FirstOrDefaultAsync(b => b.WorkspaceId == workspaceId, cancellationToken);
            var budget = budgetRow is null
                ? null
                : new AiBudgetView(budgetRow.MonthlyTokenLimit, budgetRow.TokensUsedThisMonth, budgetRow.BudgetResetDate, budgetRow.IsHardCapped);

            var wsConfig = await _db.WorkspaceAiConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.WorkspaceId == workspaceId, cancellationToken);
            var overrides = ParseOverrides(wsConfig?.FunctionOverridesJson);
            var platformConfigs = await _db.PlatformAiConfigs.AsNoTracking().Where(c => c.IsEnabled).ToListAsync(cancellationToken);

            var functions = new List<AiFunctionResolution>();
            foreach (var functionId in AiFunctionIds.All)
            {
                if (overrides.TryGetValue(functionId, out var ov))
                {
                    functions.Add(new AiFunctionResolution(functionId, ov.ModelId, ov.Provider, "workspace"));
                    continue;
                }
                var platform = platformConfigs.FirstOrDefault(p => p.FunctionId == functionId);
                if (platform is not null)
                {
                    functions.Add(new AiFunctionResolution(functionId, platform.ModelId, platform.Provider, "platform"));
                }
            }

            _logger.LogDebug("WorkspaceAiConfigService.GetAsync exit workspaceId={WorkspaceId} functions={Count}", workspaceId, functions.Count);
            return new AiConfigView(allowedProviders, budget, functions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceAiConfigService.GetAsync error workspaceId={WorkspaceId}", workspaceId);
            throw;
        }
    }

    public async Task UpdateAsync(Guid workspaceId, IReadOnlyList<FunctionOverrideInput> overrides, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WorkspaceAiConfigService.UpdateAsync enter workspaceId={WorkspaceId} overrides={Count}", workspaceId, overrides.Count);
        try
        {
            var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken)
                ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");

            var allowedProviders = await ResolveAllowedProvidersAsync(workspace.OrgId, cancellationToken);

            foreach (var ov in overrides)
            {
                if (!allowedProviders.Contains(ov.Provider))
                {
                    throw new ConfigViolationException(
                        "AI_PROVIDER_NOT_ALLOWED",
                        $"Provider '{ov.Provider}' is not enabled for your organisation. " +
                        $"Allowed providers: {string.Join(", ", allowedProviders)}. " +
                        "Ask your org owner to enable it, or pick an allowed provider.");
                }

                var model = await _db.ModelCatalogue.AsNoTracking().FirstOrDefaultAsync(m => m.ModelId == ov.ModelId, cancellationToken)
                    ?? throw new ConfigViolationException(
                        "AI_MODEL_UNKNOWN",
                        $"Model '{ov.ModelId}' is not in the model catalogue. Pick a catalogued model.");

                if (!string.Equals(model.Provider, ov.Provider, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ConfigViolationException(
                        "AI_MODEL_PROVIDER_MISMATCH",
                        $"Model '{ov.ModelId}' belongs to provider '{model.Provider}', not '{ov.Provider}'.");
                }

                ModelCapabilityValidator.Validate(ov.FunctionId, model);
            }

            var config = await _db.WorkspaceAiConfigs.FirstOrDefaultAsync(c => c.WorkspaceId == workspaceId, cancellationToken);
            var merged = MergeOverrides(config?.FunctionOverridesJson, overrides);

            if (config is null)
            {
                _db.WorkspaceAiConfigs.Add(new WorkspaceAiConfig { WorkspaceId = workspaceId, FunctionOverridesJson = merged });
            }
            else
            {
                config.FunctionOverridesJson = merged;
                config.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("WorkspaceAiConfigService.UpdateAsync exit workspaceId={WorkspaceId} applied={Count}", workspaceId, overrides.Count);
        }
        catch (Exception ex) when (ex is not ConfigViolationException)
        {
            _logger.LogError(ex, "WorkspaceAiConfigService.UpdateAsync error workspaceId={WorkspaceId}", workspaceId);
            throw;
        }
    }

    private async Task<IReadOnlyList<string>> ResolveAllowedProvidersAsync(Guid orgId, CancellationToken cancellationToken)
    {
        var orgConfig = await _db.OrgAiConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.OrgId == orgId, cancellationToken);
        if (orgConfig is { ProvidersEnabled.Count: > 0 })
        {
            return orgConfig.ProvidersEnabled;
        }

        // Fall back to the org's plan allowlist (FUNCTIONAL.md §13.2).
        var org = await _db.Organisations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId, cancellationToken);
        if (org is null) return [AiProviders.Anthropic];
        var plan = await _db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == org.PlanId, cancellationToken);
        return plan?.Limits.AllowedAiProviders is { Count: > 0 } providers ? providers : [AiProviders.Anthropic];
    }

    private static Dictionary<string, FunctionOverrideRow> ParseOverrides(string? json)
    {
        var result = new Dictionary<string, FunctionOverrideRow>();
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return result;
        var node = JsonNode.Parse(json)?.AsObject();
        if (node is null) return result;
        foreach (var (key, value) in node)
        {
            var modelId = value?["modelId"]?.GetValue<string>();
            var provider = value?["provider"]?.GetValue<string>();
            if (modelId is not null && provider is not null)
            {
                result[key] = new FunctionOverrideRow(modelId, provider);
            }
        }
        return result;
    }

    private static string MergeOverrides(string? existingJson, IReadOnlyList<FunctionOverrideInput> overrides)
    {
        var root = (string.IsNullOrWhiteSpace(existingJson) ? null : JsonNode.Parse(existingJson)?.AsObject()) ?? new JsonObject();
        foreach (var ov in overrides)
        {
            var keySource = Enum.TryParse<AiKeySource>(ov.KeySource, ignoreCase: true, out var parsed) ? parsed : AiKeySource.Platform;
            root[ov.FunctionId] = new JsonObject
            {
                ["modelId"] = ov.ModelId,
                ["provider"] = ov.Provider,
                ["keySource"] = keySource.ToString(),
            };
        }
        return root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private sealed record FunctionOverrideRow(string ModelId, string Provider);
}
