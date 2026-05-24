namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Records AI token usage to <c>ai_token_usage</c>. Implementation MUST be fire-and-forget —
/// the caller awaits nothing and the request path is never blocked by metering writes
/// (FUNCTIONAL.md §13.3 async side-channel rule).
/// </summary>
public interface IAiTokenMeteringService
{
    /// <summary>
    /// Queue a metering record for asynchronous write. Returns immediately.
    /// Errors during the background write are logged with the correlation ID; they never
    /// throw or propagate. Execution continues even if the metering store is down.
    /// </summary>
    void RecordUsage(
        string functionId,
        string modelId,
        string provider,
        Guid? orgId,
        Guid? workspaceId,
        int tokensInput,
        int tokensOutput,
        decimal costUsd);
}
