namespace Flowamaz.Core.Enums;

/// <summary>How a workflow definition can be triggered. Stored as a string column.</summary>
public enum WorkflowTriggerType
{
    Webhook,
    Form,
    Schedule,
    Manual,
    SubWorkflow,
}
