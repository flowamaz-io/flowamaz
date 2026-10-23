namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Raised by a connector operation handler when the vendor call fails or the input is unusable.
/// <see cref="Retryable"/> tells the engine whether the node's RetryPolicy should apply (rate limits,
/// 5xx, network) or the failure is permanent (bad input, auth, 4xx). The message is shown in the run
/// history, so it must be actionable and must never contain secret material.
/// </summary>
public sealed class ConnectorOperationException : Exception
{
    public ConnectorOperationException(string message, bool retryable = false, Exception? inner = null)
        : base(message, inner)
    {
        Retryable = retryable;
    }

    public bool Retryable { get; }

    /// <summary>Maps an HTTP status from a vendor API to retryability (429 and 5xx are transient).</summary>
    public static bool IsRetryableStatus(int status) => status == 429 || status >= 500;
}

/// <summary>
/// Raised when a node needs a credential that cannot be resolved (missing, ambiguous, revoked or
/// belonging to another connector). Never retryable — the user must fix the workflow or the vault.
/// </summary>
public sealed class ConnectorCredentialException : Exception
{
    public ConnectorCredentialException(string message) : base(message) { }
}
