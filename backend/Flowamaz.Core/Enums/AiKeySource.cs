namespace Flowamaz.Core.Enums;

/// <summary>
/// Where the API key for an AI call originates. Resolution per FUNCTIONAL.md §5.3.
/// </summary>
public enum AiKeySource
{
    /// <summary>Flowamaz-managed platform key (Anthropic, Google) — billed to platform.</summary>
    Platform = 0,

    /// <summary>Bring-your-own-key — stored encrypted in workspace credential vault, billed to customer's provider account.</summary>
    Byok = 1,
}
