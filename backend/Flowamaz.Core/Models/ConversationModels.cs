namespace Flowamaz.Core.Models;

// Conversation → Workflow (FUNCTIONAL.md §6.4, §8.4). Extraction is a separate step from generation so the user
// can review and correct what the AI read out of the thread before a workflow is generated from it.

/// <summary>Someone who wrote in the conversation, found by parsing the thread (no AI involved).</summary>
public sealed record ConversationParticipant(string Name, int MessageCount);

/// <summary>One step of the process. <see cref="SaidBy"/> is who proposed or described it in the thread.</summary>
public sealed record ConversationStep(string Title, string? Owner = null, string? System = null, string? SaidBy = null);

/// <summary>A condition the thread agreed on, e.g. "amount above $5,000" → "CFO approves".</summary>
public sealed record ConversationDecision(string Condition, string Outcome, string? SaidBy = null);

/// <summary>Who has to approve, and when (e.g. "Rajan", "up to $5,000").</summary>
public sealed record ConversationApprover(string Who, string? When = null);

/// <summary>What the AI read out of a conversation, as the user reviews and edits it before generation.</summary>
public sealed record ConversationExtraction(
    string WorkflowName,
    string Summary,
    string Trigger,
    IReadOnlyList<ConversationParticipant> Participants,
    IReadOnlyList<ConversationStep> Steps,
    IReadOnlyList<ConversationDecision> Decisions,
    IReadOnlyList<ConversationApprover> Approvers,
    IReadOnlyList<string> Agreements,
    IReadOnlyList<string> Systems,
    IReadOnlyList<string> OpenQuestions,
    double Confidence);

/// <summary>
/// Result of POST /workflows/conversation/preview. Nothing is saved. <see cref="Warnings"/> are things the user
/// should check before generating (an approver nobody in the thread mentioned, low confidence, …).
/// </summary>
public sealed record ConversationPreviewResult(
    ConversationExtraction Extraction,
    IReadOnlyList<string> Warnings,
    int TokensUsed);
