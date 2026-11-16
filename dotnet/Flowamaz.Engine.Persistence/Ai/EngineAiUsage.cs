using Flowamaz.Core.Entities;

namespace Flowamaz.Infrastructure.Persistence.Ai;

/// <summary>
/// One AI call made by the open runtime (table <c>engine_ai_usage</c>): workspace, run, step, provider,
/// model, function, tokens in and out, cost, and whether it succeeded. Written by <see cref="EfAiUsageMeter"/>.
/// The Flowamaz product meters into its own <c>ai_token_usage</c> table and leaves this one out of its model.
/// </summary>
public sealed class EngineAiUsage : WorkspaceEntity
{
    public Guid? InstanceId { get; set; }
    public string? NodeId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string FunctionId { get; set; } = string.Empty;
    public int TokensInput { get; set; }
    public int TokensOutput { get; set; }
    public decimal CostUsd { get; set; }
    public bool Succeeded { get; set; }
}
