namespace Flowamaz.Core.Interfaces.Runtime;

/// <summary>
/// Runs one model completion for an AI step. The open runtime ships an Anthropic provider that uses the
/// host's own API key (BYOK); a host can register any other. The provider reports the tokens the model
/// actually used, so metering is exact.
/// </summary>
public interface IAiModelProvider
{
    /// <summary>Provider name recorded with usage ("anthropic", …).</summary>
    string Name { get; }

    /// <summary>
    /// The model a step runs on when it names none (from configuration; never hard-coded). Null when the
    /// host configured none: the step then fails with a message saying how to set one.
    /// </summary>
    string? DefaultModel { get; }

    Task<AiCompletion> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default);
}

public sealed record AiCompletionRequest(
    Guid WorkspaceId,
    string Model,
    string? SystemPrompt,
    string Prompt,
    int MaxTokens);

/// <param name="CostUsd">What the call cost, when the provider knows its prices (configured); otherwise null.</param>
public sealed record AiCompletion(string Text, string Model, int InputTokens, int OutputTokens, decimal? CostUsd);

/// <summary>
/// A provider failure. <see cref="Retryable"/>: rate limits, overload, server errors and timeouts (the
/// step is retried per its retry policy); otherwise configuration or request problems that won't fix
/// themselves (no key, a rejected key, an unknown model).
/// </summary>
public sealed class AiProviderException : Exception
{
    public AiProviderException(string message, bool retryable, Exception? inner = null) : base(message, inner) => Retryable = retryable;

    public bool Retryable { get; }
}

/// <summary>
/// Records every AI call: tokens in and out, model, function, workspace and cost (Flowamaz rule 3:
/// no AI call without metering). Required: the open runtime has no no-op meter, and an AI step fails
/// rather than run unmetered. <c>Flowamaz.Engine.Persistence</c> stores records in <c>engine_ai_usage</c>.
/// </summary>
public interface IAiUsageMeter
{
    Task RecordAsync(AiUsageRecord record, CancellationToken cancellationToken = default);
}

/// <param name="FunctionId">Which Flowamaz AI function made the call; AI steps are <see cref="AiFunctions.NodeExec"/>.</param>
/// <param name="Succeeded">False for a call that failed after reaching the provider (tokens may still be counted).</param>
public sealed record AiUsageRecord(
    Guid WorkspaceId,
    Guid? InstanceId,
    string? NodeId,
    string Provider,
    string Model,
    string FunctionId,
    int TokensInput,
    int TokensOutput,
    decimal CostUsd,
    bool Succeeded);

/// <summary>Flowamaz AI function ids used in metering.</summary>
public static class AiFunctions
{
    /// <summary>F4: an AI step in a workflow run.</summary>
    public const string NodeExec = "node-exec";
}
