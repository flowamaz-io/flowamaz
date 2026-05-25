using Flowamaz.Core.Entities.Ai;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Fire-and-forget metering. <see cref="RecordUsage"/> returns immediately;
/// the actual DB write happens on a background <c>Task.Run</c> with a fresh DI scope so the
/// scoped DbContext lifetime is honoured (the request scope may have ended by then).
/// Errors are caught and logged — they never propagate, never throw.
/// </summary>
public sealed class AiTokenMeteringService : IAiTokenMeteringService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiTokenMeteringService> _logger;

    public AiTokenMeteringService(
        IServiceScopeFactory scopeFactory,
        ILogger<AiTokenMeteringService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void RecordUsage(
        string functionId,
        string modelId,
        string provider,
        Guid? orgId,
        Guid? workspaceId,
        int tokensInput,
        int tokensOutput,
        decimal costUsd)
    {
        _logger.LogDebug(
            "AiTokenMeteringService.RecordUsage enter func={FunctionId} model={ModelId} provider={Provider} workspace={WorkspaceId} tokensIn={TokensInput} tokensOut={TokensOutput} cost={CostUsd}",
            functionId, modelId, provider, workspaceId, tokensInput, tokensOutput, costUsd);

        var record = new AiTokenUsage
        {
            FunctionId = functionId,
            ModelId = modelId,
            Provider = provider,
            OrgId = orgId,
            WorkspaceId = workspaceId,
            TokensInput = tokensInput,
            TokensOutput = tokensOutput,
            CostUsd = costUsd,
        };

        // Fire-and-forget — do NOT await. The request path must never wait for metering.
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
                await db.AiTokenUsage.AddAsync(record);
                await db.SaveChangesAsync();
                _logger.LogDebug(
                    "AiTokenMeteringService.RecordUsage exit id={UsageId} workspace={WorkspaceId}",
                    record.Id, workspaceId);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _logger.LogError(ex,
                    "AiTokenMeteringService.RecordUsage error — metering record dropped (func={FunctionId} workspace={WorkspaceId})",
                    functionId, workspaceId);
            }
        });
    }
}
