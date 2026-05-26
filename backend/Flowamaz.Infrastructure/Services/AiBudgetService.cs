using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services;

public sealed class AiBudgetService : IAiBudgetService
{
    private readonly FlowAmazDbContext _db;
    private readonly ILogger<AiBudgetService> _log;

    public AiBudgetService(FlowAmazDbContext db, ILogger<AiBudgetService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<bool> IsBudgetAvailableAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        _log.LogInformation("AiBudgetService.IsBudgetAvailableAsync entry workspaceId={WorkspaceId}", workspaceId);

        var budget = await _db.WorkspaceAiBudgets
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.WorkspaceId == workspaceId, cancellationToken);

        // No budget row = unconfigured workspace = unrestricted
        if (budget is null)
        {
            _log.LogInformation("AiBudgetService.IsBudgetAvailableAsync exit unconfigured=true");
            return true;
        }

        // If not hard-capped, always pass through
        if (!budget.IsHardCapped)
        {
            _log.LogInformation("AiBudgetService.IsBudgetAvailableAsync exit hard_capped=false");
            return true;
        }

        var available = budget.TokensUsedThisMonth < budget.MonthlyTokenLimit;
        _log.LogInformation("AiBudgetService.IsBudgetAvailableAsync exit available={Available} used={Used} limit={Limit}",
            available, budget.TokensUsedThisMonth, budget.MonthlyTokenLimit);
        return available;
    }
}
