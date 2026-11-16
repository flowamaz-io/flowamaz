namespace Flowamaz.Core.Enums;

/// <summary>Channel a gate request is delivered through. Stored as a string column.</summary>
public enum GateDeliveryChannel
{
    Slack,
    Teams,
    Email,
    Portal,
}
