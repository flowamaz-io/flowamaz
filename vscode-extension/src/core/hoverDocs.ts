/**
 * Documentation lookup for Flowamaz workflow YAML fields and node types.
 * Pure module — no `vscode` import — so it is unit-testable without Electron.
 */

export interface DocEntry {
  /** Short title shown as the heading of the hover. */
  title: string;
  /** Markdown body describing the field or node type. */
  doc: string;
}

/**
 * Field-level documentation. These are the YAML keys that appear inside a
 * workflow definition (`type`, `retry`, `timeout`, ...).
 */
const FIELD_DOCS: Record<string, DocEntry> = {
  type: {
    title: "type",
    doc: "The node type. One of: `action`, `ai`, `human-gate`, `router`, `end`, `trigger`, `parallel`, `foreach`, `trycatch`.",
  },
  retry: {
    title: "retry",
    doc: "Retry policy for the node. Accepts a count or an object `{ maxAttempts, backoff }`. Failed steps are retried before the node fails.",
  },
  timeout: {
    title: "timeout",
    doc: "Maximum duration the node may run before it is cancelled, e.g. `30s`, `5m`. Applies per-node.",
  },
  condition: {
    title: "condition",
    doc: "A boolean expression evaluated against the instance context. On a `router` node it selects an outgoing edge; on an edge it gates traversal.",
  },
  label: {
    title: "label",
    doc: "Human-readable name for the node, shown on the SFG canvas and in run logs.",
  },
  id: {
    title: "id",
    doc: "Stable, unique identifier for the node, edge, or workflow. Referenced by edges (`from`/`to`).",
  },
  config: {
    title: "config",
    doc: "Node-specific configuration object. Shape depends on `type` — e.g. an `ai` node takes `prompt`/`model`, an `action` node takes connector parameters.",
  },
  edges: {
    title: "edges",
    doc: "List of directed connections between nodes. Each edge has `id`, `from`, `to`, and optional `condition`.",
  },
  nodes: {
    title: "nodes",
    doc: "List of workflow nodes. Each node has `id`, `type`, `label`, and `config`.",
  },
};

/**
 * Node-type documentation. The `type:` value (e.g. `action`, `ai`) resolves to
 * one of these entries.
 */
const NODE_TYPE_DOCS: Record<string, DocEntry> = {
  action: {
    title: "action node",
    doc: "Invokes a connector or built-in operation (HTTP call, database write, transform). Side-effecting step in the workflow.",
  },
  ai: {
    title: "ai node",
    doc: "Calls a Flowamaz-governed AI model. Model resolves node → workflow → workspace → org → platform. Every call is metered.",
  },
  "human-gate": {
    title: "human-gate node",
    doc: "Pauses the instance until a human approves, rejects, or provides input. Supports assignees, SLA timeouts, and escalation.",
  },
  router: {
    title: "router node",
    doc: "Branches execution based on `condition` expressions, sending the instance down the first matching outgoing edge.",
  },
  end: {
    title: "end node",
    doc: "Terminal node. Completes the instance with an optional status/result. A workflow must have at least one `end` node.",
  },
  trigger: {
    title: "trigger node",
    doc: "Entry point of the workflow (webhook, schedule, event, or manual). A workflow must have exactly one `trigger`.",
  },
  parallel: {
    title: "parallel node",
    doc: "Fans execution out across multiple branches that run concurrently, then joins when all branches complete.",
  },
  foreach: {
    title: "foreach node",
    doc: "Iterates over a collection, running its body once per item. Supports optional concurrency limits.",
  },
  trycatch: {
    title: "trycatch node",
    doc: "Runs a protected body and routes to a catch branch on failure, enabling structured error handling.",
  },
};

/**
 * Return Markdown documentation for a workflow word (a field key or a node
 * type value). Returns `undefined` when the word is not known.
 */
export function getHoverDoc(word: string): string | undefined {
  const key = word.trim().toLowerCase();
  const entry = NODE_TYPE_DOCS[key] ?? FIELD_DOCS[key];
  if (!entry) {
    return undefined;
  }
  return "**" + entry.title + "**\n\n" + entry.doc;
}
