namespace Flowamaz.Core.Models;

/// <summary>
/// Outcome of validating an incoming API key — returned only when the key is active and unexpired.
/// Carries just enough to scope the request: the workspace, environment, granted scopes and key id.
/// </summary>
public sealed record WorkspaceApiKeyValidationResult(
    Guid KeyId,
    Guid WorkspaceId,
    Guid EnvironmentId,
    IReadOnlyList<string> Scopes);
