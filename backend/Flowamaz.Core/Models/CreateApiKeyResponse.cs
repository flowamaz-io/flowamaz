using Flowamaz.Core.Entities.Workspaces;

namespace Flowamaz.Core.Models;

/// <summary>
/// Result of minting an API key. <see cref="PlainKey"/> is the full key in clear text and is
/// the ONLY time it is ever available — it is not stored and cannot be recovered. Surface it to
/// the caller once, then discard. Never log it (FUNCTIONAL.md §12.5).
/// </summary>
public sealed class CreateApiKeyResponse
{
    public required WorkspaceApiKey ApiKey { get; init; }
    public required string PlainKey { get; init; }
}
