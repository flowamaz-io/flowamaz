/**
 * Static graph analysis for Flowamaz workflow YAML.
 * Pure module — parses with js-yaml, returns plain diagnostic objects.
 * No `vscode` import, so it is fully unit-testable.
 */

import * as yaml from "js-yaml";

export type DiagnosticSeverity = "error" | "warning";

export interface Diagnostic {
  message: string;
  /** Zero-based line index the issue is anchored to. 0 when unknown. */
  line: number;
  severity: DiagnosticSeverity;
}

interface NodeDef {
  id?: unknown;
  type?: unknown;
}

interface EdgeDef {
  from?: unknown;
  to?: unknown;
}

interface WorkflowDoc {
  nodes?: unknown;
  edges?: unknown;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function asString(value: unknown): string | undefined {
  return typeof value === "string" ? value : undefined;
}

/**
 * Find the zero-based line index where `id: <value>` first appears, so a
 * diagnostic can be anchored near the offending node. Returns 0 when not found.
 */
function findIdLine(text: string, id: string): number {
  const lines = text.split(/\r?\n/);
  const needle = "id: " + id;
  for (let i = 0; i < lines.length; i++) {
    const stripped = lines[i].replace(/['"]/g, "");
    if (stripped.includes(needle)) {
      return i;
    }
  }
  return 0;
}

/**
 * Analyse workflow YAML for structural graph problems:
 *  - missing trigger node
 *  - missing end node
 *  - edge referencing a node id that does not exist
 *  - orphaned node (never referenced as an edge `to`, and not the trigger)
 *
 * Malformed YAML returns a single parse-error diagnostic. A non-workflow
 * document (no `nodes`) returns no diagnostics.
 */
export function findGraphDiagnostics(yamlText: string): Diagnostic[] {
  if (yamlText.trim().length === 0) {
    return [];
  }

  let doc: unknown;
  try {
    doc = yaml.load(yamlText);
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    return [{ message: "YAML parse error: " + message, line: 0, severity: "error" }];
  }

  if (!isRecord(doc)) {
    return [];
  }

  const wf = doc as WorkflowDoc;
  if (!Array.isArray(wf.nodes)) {
    // Not a workflow document we can analyse.
    return [];
  }

  const nodes = (wf.nodes as unknown[]).filter(isRecord) as NodeDef[];
  const edges = Array.isArray(wf.edges)
    ? ((wf.edges as unknown[]).filter(isRecord) as EdgeDef[])
    : [];

  const diagnostics: Diagnostic[] = [];

  const nodeIds = new Set<string>();
  const triggerIds = new Set<string>();
  let hasTrigger = false;
  let hasEnd = false;

  for (const node of nodes) {
    const id = asString(node.id);
    const type = asString(node.type);
    if (id !== undefined) {
      nodeIds.add(id);
    }
    if (type === "trigger") {
      hasTrigger = true;
      if (id !== undefined) {
        triggerIds.add(id);
      }
    }
    if (type === "end") {
      hasEnd = true;
    }
  }

  if (!hasTrigger) {
    diagnostics.push({
      message: "Workflow has no trigger node. Add a node with `type: trigger` as the entry point.",
      line: 0,
      severity: "error",
    });
  }
  if (!hasEnd) {
    diagnostics.push({
      message: "Workflow has no end node. Add a node with `type: end` so instances can complete.",
      line: 0,
      severity: "error",
    });
  }

  // Edges referencing missing nodes.
  const referencedAsTarget = new Set<string>();
  for (const edge of edges) {
    const from = asString(edge.from);
    const to = asString(edge.to);
    if (to !== undefined) {
      referencedAsTarget.add(to);
    }
    if (from !== undefined && !nodeIds.has(from)) {
      diagnostics.push({
        message: 'Edge references missing node "' + from + '" in its `from`. No node defines that id.',
        line: 0,
        severity: "error",
      });
    }
    if (to !== undefined && !nodeIds.has(to)) {
      diagnostics.push({
        message: 'Edge references missing node "' + to + '" in its `to`. No node defines that id.',
        line: 0,
        severity: "error",
      });
    }
  }

  // Orphaned nodes: never an edge target and not a trigger.
  for (const node of nodes) {
    const id = asString(node.id);
    if (id === undefined) {
      continue;
    }
    if (triggerIds.has(id)) {
      continue;
    }
    if (!referencedAsTarget.has(id)) {
      diagnostics.push({
        message: 'Node "' + id + '" is orphaned: no edge points to it and it is not the trigger.',
        line: findIdLine(yamlText, id),
        severity: "warning",
      });
    }
  }

  return diagnostics;
}
