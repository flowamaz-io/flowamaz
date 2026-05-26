namespace Flowamaz.Core.Enums;

/// <summary>Categories of empathy issues a workflow analysis can surface.</summary>
public enum EmpathyIssueType
{
    NoStatusUpdate = 0,
    LongWait = 1,
    MultipleEmails = 2,
    NoOutcomeNotification = 3,
    VisibilityGap = 4,
}
