namespace Flowamaz.Core.Constants;

/// <summary>
/// Canonical string identifiers for the 7 platform AI functions (FUNCTIONAL.md §5.2).
/// Stored in DB as-is; never hardcode in business logic — resolve via IModelResolutionService.
/// </summary>
public static class AiFunctionIds
{
    public const string Copilot = "copilot";          // F1: in-canvas assistant
    public const string NlYaml = "nl-yaml";           // F2: NL→YAML generation
    public const string VisualInput = "visual-input"; // F3: image→workflow (vision required)
    public const string NodeExec = "node-exec";       // F4: workflow node execution
    public const string ProcessIntel = "process-intel"; // F5: batch analytics
    public const string HelpAssist = "help-assist";   // F6: in-app help Q&A
    public const string DocParse = "doc-parse";       // F7: document/SOP parsing (≥100k ctx)

    public static readonly IReadOnlyList<string> All =
        [Copilot, NlYaml, VisualInput, NodeExec, ProcessIntel, HelpAssist, DocParse];
}
