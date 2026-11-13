namespace Flowamaz.Core.Exceptions;

/// <summary>
/// The AI could not read a process out of a pasted conversation (its answer was not the expected JSON, or it found
/// no steps). Nothing was generated or saved.
/// </summary>
public sealed class ConversationExtractionException : AppException
{
    private ConversationExtractionException(string message) : base("CONVERSATION_EXTRACTION_FAILED", message, httpStatusCode: 422)
    {
    }

    public static ConversationExtractionException Unreadable() => new(
        "The AI's answer about this conversation couldn't be read, so no workflow was generated. " +
        "Results vary between attempts, so try Extract again. If it keeps failing, describe the process in plain English instead.");

    public static ConversationExtractionException NoSteps() => new(
        "No process steps were found in this conversation, so no workflow was generated. " +
        "Paste the part of the thread where the steps are discussed (who does what, in which order), or describe the process in plain English instead.");
}
