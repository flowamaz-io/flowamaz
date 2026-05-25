namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when workflow YAML fails to parse or violates SFG structural rules (missing trigger,
/// orphaned node, dangling edge). Message names the offending node/field so the author can fix it.
/// Maps to 422 — the request was understood but the workflow is invalid.
/// </summary>
public sealed class SfgParseException : AppException
{
    public SfgParseException(string message)
        : base("SFG_PARSE_ERROR", message, httpStatusCode: 422) { }

    public SfgParseException(string message, Exception inner)
        : base("SFG_PARSE_ERROR", message, inner, httpStatusCode: 422) { }
}
