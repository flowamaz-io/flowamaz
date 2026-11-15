namespace Flowamaz.Core.Enums;

/// <summary>The node kinds the SFG (Smart Flow Graph) supports (FUNCTIONAL.md §6.6).</summary>
public enum NodeType
{
    Trigger,
    Action,
    Ai,
    HumanGate,
    Router,
    End,
    ForEach,
    Parallel,
    TryCatch,
    Wait,
    IfElse,
    Switch,
    While,
    SubWorkflow,
}
