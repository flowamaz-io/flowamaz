namespace Flowamaz.Core.Enums;

/// <summary>What initiated a workflow instance. Stored as a string column.</summary>
public enum InstanceTriggerType
{
    Manual,
    Webhook,
    Schedule,
    SubWorkflow,
}
