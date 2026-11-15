/**
 * Snippet definitions for Flowamaz workflow YAML.
 * Pure module — no `vscode` import — so the same defs power the completion
 * provider, the generated `.code-snippets` file, and unit tests.
 */

export interface SnippetDef {
  /** Trigger text / completion label, e.g. `fmz-action`. */
  label: string;
  /** Snippet body, one entry per line. May contain `${n:placeholder}` stops. */
  body: string[];
  /** One-line description shown in the completion list. */
  description: string;
}

const SNIPPETS: SnippetDef[] = [
  {
    label: "fmz-workflow",
    description: "Scaffold a complete Flowamaz workflow document",
    body: [
      "kind: Workflow",
      "workflow:",
      "  id: ${1:my-workflow}",
      "  version: 1",
      "  name: ${2:My Workflow}",
      "nodes:",
      "  - id: start",
      "    type: trigger",
      "    label: Start",
      "    config: {}",
      "  - id: done",
      "    type: end",
      "    label: Done",
      "    config: {}",
      "edges:",
      "  - id: e1",
      "    from: start",
      "    to: done",
      "",
    ],
  },
  {
    label: "fmz-action",
    description: "Action node (connector / built-in operation)",
    body: [
      "- id: ${1:action-1}",
      "  type: action",
      "  label: ${2:Do something}",
      "  retry: ${3:1}",
      "  timeout: ${4:30s}",
      "  config:",
      "    connector: ${5:http}",
      "    operation: ${6:request}",
      "",
    ],
  },
  {
    label: "fmz-ai",
    description: "AI node (governed, metered model call)",
    body: [
      "- id: ${1:ai-1}",
      "  type: ai",
      "  label: ${2:Classify}",
      "  config:",
      "    prompt: ${3:Summarise the input.}",
      "    function: ${4:F4}",
      "",
    ],
  },
  {
    label: "fmz-gate",
    description: "Human-gate node (approval / input pause)",
    body: [
      "- id: ${1:gate-1}",
      "  type: human-gate",
      "  label: ${2:Approval}",
      "  timeout: ${3:24h}",
      "  config:",
      "    assignees: [${4:owner}]",
      "",
    ],
  },
  {
    label: "fmz-router",
    description: "Router node (conditional branching)",
    body: [
      "- id: ${1:router-1}",
      "  type: router",
      "  label: ${2:Branch}",
      "  config:",
      "    condition: ${3:context.amount > 1000}",
      "",
    ],
  },
  {
    label: "fmz-trycatch",
    description: "Try/catch node (structured error handling)",
    body: [
      "- id: ${1:trycatch-1}",
      "  type: trycatch",
      "  label: ${2:Protected}",
      "  config:",
      "    try: ${3:body-node-id}",
      "    catch: ${4:catch-node-id}",
      "",
    ],
  },
  {
    label: "fmz-parallel",
    description: "Parallel node (concurrent fan-out / join)",
    body: [
      "- id: ${1:parallel-1}",
      "  type: parallel",
      "  label: ${2:Fan out}",
      "  config:",
      "    branches: [${3:branch-a}, ${4:branch-b}]",
      "",
    ],
  },
  {
    label: "fmz-foreach",
    description: "Foreach node (iterate over a collection)",
    body: [
      "- id: ${1:foreach-1}",
      "  type: foreach",
      "  label: ${2:For each item}",
      "  config:",
      "    items: ${3:context.items}",
      "    body: ${4:body-node-id}",
      "",
    ],
  },
];

/** Return all Flowamaz snippet definitions. */
export function getSnippets(): SnippetDef[] {
  return SNIPPETS;
}

/**
 * Build the VS Code `.code-snippets` JSON object from the defs. Pure helper
 * used by a build step / tooling; keeps a single source of truth.
 */
export function buildCodeSnippetsFile(): Record<
  string,
  { prefix: string; body: string[]; description: string }
> {
  const out: Record<
    string,
    { prefix: string; body: string[]; description: string }
  > = {};
  for (const s of SNIPPETS) {
    out[s.label] = {
      prefix: s.label,
      body: s.body,
      description: s.description,
    };
  }
  return out;
}
