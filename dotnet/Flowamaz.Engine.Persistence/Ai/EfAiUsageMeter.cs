using Flowamaz.Core.Interfaces.Runtime;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Persistence.Ai;

/// <summary>
/// <see cref="IAiUsageMeter"/> that writes each AI call to <c>engine_ai_usage</c> straight away (its own
/// save), so a step that fails later still leaves its usage recorded. It runs between orchestrator
/// steps, when the scope has nothing else pending.
/// </summary>
public sealed class EfAiUsageMeter : IAiUsageMeter
{
    private readonly EngineDbContext _db;
    private readonly ILogger<EfAiUsageMeter> _logger;

    public EfAiUsageMeter(EngineDbContext db, ILogger<EfAiUsageMeter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RecordAsync(AiUsageRecord record, CancellationToken cancellationToken = default)
    {
        _db.Set<EngineAiUsage>().Add(new EngineAiUsage
        {
            WorkspaceId = record.WorkspaceId,
            InstanceId = record.InstanceId,
            NodeId = record.NodeId,
            Provider = record.Provider,
            ModelId = record.Model,
            FunctionId = record.FunctionId,
            TokensInput = Math.Max(0, record.TokensInput),
            TokensOutput = Math.Max(0, record.TokensOutput),
            CostUsd = Math.Max(0m, record.CostUsd),
            Succeeded = record.Succeeded,
        });
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "EfAiUsageMeter recorded workspace={WorkspaceId} model={Model} function={Function} tokensIn={In} tokensOut={Out} costUsd={Cost}",
            record.WorkspaceId, record.Model, record.FunctionId, record.TokensInput, record.TokensOutput, record.CostUsd);
    }
}
