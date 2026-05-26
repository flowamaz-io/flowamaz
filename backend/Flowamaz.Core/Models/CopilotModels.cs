namespace Flowamaz.Core.Models;

public sealed record CopilotResult(
    bool Success,
    string? YamlPatch,
    string? MatchedPattern,
    bool CacheHit,
    int? TokensUsed,
    string? ErrorMessage);

public sealed record SopParseResult(
    string YamlContent,
    IReadOnlyList<string> ExtractedSteps,
    int PageCount,
    int WordCount,
    int TokensUsed);
