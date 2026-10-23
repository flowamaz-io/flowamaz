namespace Flowamaz.Core.Models;

/// <summary>Topics the NL clarification step can ask about (FUNCTIONAL.md §6.1 — few questions, asked at once).</summary>
public static class ClarificationTopics
{
    public const string Trigger = "trigger";
    public const string Approver = "approver";
    public const string Rejection = "rejection";
    public const string Systems = "systems";
    public const string Other = "other";
}

/// <summary>A question shown to the user before NL generation.</summary>
public sealed record ClarifyingQuestion(string Id, string Topic, string Question, string? Hint = null);

/// <summary>The user's answer to a clarifying question; appended to the generation prompt.</summary>
public sealed record ClarificationAnswer(string Topic, string Question, string Answer);

/// <summary>Up to 3 questions. Empty when the description already covers the essentials.</summary>
public sealed record ClarificationResult(
    IReadOnlyList<ClarifyingQuestion> Questions,
    bool UsedAi,
    int TokensUsed);
