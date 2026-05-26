namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Calls a BYOM (Bring Your Own Model) endpoint using an OpenAI-compatible chat completions API.
/// Credential (endpoint URL + API key) is stored in the vault and retrieved at call time.
/// </summary>
public interface IByomProviderService
{
    /// <summary>
    /// Sends a user-turn prompt to the BYOM endpoint and returns the raw text response.
    /// </summary>
    Task<string> CompleteAsync(
        Guid workspaceId,
        Guid byomCredentialId,
        string model,
        string prompt,
        int maxTokens,
        CancellationToken ct = default);
}
