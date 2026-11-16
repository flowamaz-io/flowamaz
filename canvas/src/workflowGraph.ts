import * as jsYaml from 'js-yaml';
import type { CanvasEdge, CanvasNode, CompensationBlock, RetryPolicy, TimeoutPolicy } from './types';

type Dict = Record<string, unknown>;

/**
 * Canvas node types keyed by the type with case, dashes and underscores removed, so YAML written in the engine's
 * spelling (`HumanGate`, `TryCatch`, `human_gate`) loads as the canvas type (`human-gate`, `try-catch`).
 */
const CANVAS_TYPES: Record<string, CanvasNode['type']> = {
  trigger: 'trigger', action: 'action', ai: 'ai', humangate: 'human-gate', router: 'router', end: 'end',
  foreach: 'foreach', parallel: 'parallel', trycatch: 'try-catch', wait: 'wait', subworkflow: 'sub-workflow',
  annotation: 'annotation',
};

/** The canvas type for a YAML node type; a type the canvas has no shape for is kept as written. */
export function canvasNodeType(raw: string | undefined): CanvasNode['type'] {
  const value = raw?.trim();
  if (!value) return 'action';
  return CANVAS_TYPES[value.toLowerCase().replace(/[-_\s]/g, '')] ?? (value as CanvasNode['type']);
}

const isDict = (v: unknown): v is Dict => typeof v === 'object' && v !== null && !Array.isArray(v);
const str = (v: unknown): string | undefined =>
  typeof v === 'string' ? v : typeof v === 'number' || typeof v === 'boolean' ? String(v) : undefined;

/**
 * The mapping that holds a workflow's `nodes` / `edges` / `variables`: `spec` in flowamaz/v1, or the document
 * itself in the short legacy shape (top-level `nodes:`). Editors must write back into this same mapping;
 * writing `spec.nodes` into a short-shape document defines every node twice (CC, PR #23).
 * With `create`, a document with neither gets an empty `spec` (flowamaz/v1).
 */
export function workflowSection(doc: unknown, create: true): Dict;
export function workflowSection(doc: unknown, create?: false): Dict | null;
export function workflowSection(doc: unknown, create = false): Dict | null {
  if (!isDict(doc)) return null;
  if (isDict(doc.spec)) return doc.spec;
  if (Array.isArray(doc.nodes) || Array.isArray(doc.edges)) return doc;
  if (!create) return null;
  const spec: Dict = {};
  doc.spec = spec;
  return spec;
}

/**
 * Extracts the canvas graph (nodes + edges) from workflow YAML. Accepts both the flowamaz/v1 shape
 * (`spec.nodes` / `spec.edges`) and the legacy top-level `nodes` / `edges`, in any valid YAML style.
 * Returns null when the text is not parseable YAML or has no nodes (the canvas then keeps its state).
 */
export function parseWorkflowGraph(yaml: string): { nodes: CanvasNode[]; edges: CanvasEdge[] } | null {
  let doc: unknown;
  try {
    doc = jsYaml.load(yaml);
  } catch {
    return null; // mid-edit / invalid YAML — the validator reports it; keep the current canvas
  }
  if (!isDict(doc)) return null;

  const spec = workflowSection(doc) ?? doc;
  const rawNodes = Array.isArray(spec.nodes) ? spec.nodes : [];
  const rawEdges = Array.isArray(spec.edges) ? spec.edges : [];

  const nodes: CanvasNode[] = [];
  for (const raw of rawNodes) {
    if (!isDict(raw)) continue;
    const id = str(raw.id)?.trim();
    if (!id) continue;
    const position = isDict(raw.position) && typeof raw.position.x === 'number' && typeof raw.position.y === 'number'
      ? { x: raw.position.x, y: raw.position.y }
      : undefined;
    nodes.push({
      id,
      type: canvasNodeType(str(raw.type)),
      label: str(raw.label)?.trim() || id,
      config: isDict(raw.config) ? raw.config : {},
      ...readNodePolicies(raw),
      ...(typeof raw.content === 'string' ? { content: raw.content } : {}),
      ...(position ? { position } : {}),
    });
  }

  const edges: CanvasEdge[] = [];
  rawEdges.forEach((raw, index) => {
    if (!isDict(raw)) return;
    const from = str(raw.from)?.trim();
    const to = str(raw.to)?.trim();
    if (!from || !to) return;
    const via = str(raw.via)?.trim();
    const condition = str(raw.condition)?.trim();
    edges.push({
      id: str(raw.id)?.trim() || `edge-${index}`,
      from,
      to,
      ...(via ? { via } : {}),
      ...(condition ? { condition } : {}),
    });
  });

  return nodes.length > 0 ? { nodes, edges } : null;
}

// ── Retry / timeout / compensate blocks ─────────────────────────────────────────────────────────────
// The documented YAML (docs/yaml-spec.md, the v1 schema) is snake_case: retry.max_attempts /
// backoff_seconds / backoff_multiplier, timeout.seconds / on_timeout, compensate.strategy / node_id.
// Older canvas versions wrote camelCase (and `compensation:`); both are read, only the documented form
// is written, so editing a node also upgrades it.

/** Keys compared without case, dashes or underscores. */
function pick(block: Dict, ...names: string[]): unknown {
  const wanted = new Set(names.map((n) => n.replace(/[-_]/g, '').toLowerCase()));
  for (const [key, value] of Object.entries(block)) {
    if (wanted.has(key.replace(/[-_]/g, '').toLowerCase()) && value !== undefined && value !== null) return value;
  }
  return undefined;
}

function num(value: unknown): number | undefined {
  const n = typeof value === 'string' ? Number(value) : value;
  return typeof n === 'number' && Number.isFinite(n) ? n : undefined;
}

/** The node's retry, timeout and compensate blocks, from either spelling. */
export function readNodePolicies(raw: Dict): Pick<CanvasNode, 'retry' | 'timeout' | 'compensate'> {
  const result: Pick<CanvasNode, 'retry' | 'timeout' | 'compensate'> = {};
  if (isDict(raw.retry)) {
    const maxAttempts = num(pick(raw.retry, 'max_attempts'));
    if (maxAttempts !== undefined) {
      result.retry = {
        maxAttempts,
        backoffSeconds: num(pick(raw.retry, 'backoff_seconds')) ?? 0,
        backoffMultiplier: num(pick(raw.retry, 'backoff_multiplier')) ?? 1,
      };
    }
  }
  if (isDict(raw.timeout)) {
    const seconds = num(pick(raw.timeout, 'seconds', 'timeout_seconds'));
    const onTimeout = str(pick(raw.timeout, 'on_timeout', 'on_timeout_node_id'));
    if (seconds !== undefined || onTimeout) {
      result.timeout = { seconds: seconds ?? 0, ...(onTimeout ? { onTimeout } : {}) };
    }
  }
  const comp = isDict(raw.compensate) ? raw.compensate : isDict(raw.compensation) ? raw.compensation : null;
  if (comp) {
    const strategy = str(pick(comp, 'strategy'));
    if (strategy === 'backward' || strategy === 'forward' || strategy === 'pivot') {
      const nodeId = str(pick(comp, 'node_id', 'compensate_node_id'));
      result.compensate = { strategy, ...(nodeId ? { nodeId } : {}) };
    }
  }
  return result;
}

/**
 * Writes policies onto a YAML node in the documented form. `undefined` leaves a block as it is; `null`
 * removes it. Older spellings of a block that is written are dropped.
 */
export function writeNodePolicies(
  node: Dict,
  policies: { retry?: RetryPolicy | null; timeout?: TimeoutPolicy | null; compensate?: CompensationBlock | null },
): void {
  if (policies.retry !== undefined) {
    if (policies.retry === null) delete node.retry;
    else {
      node.retry = {
        max_attempts: Math.max(1, Math.round(policies.retry.maxAttempts)),
        backoff_seconds: Math.max(0, Math.round(policies.retry.backoffSeconds)),
        backoff_multiplier: Math.max(1, policies.retry.backoffMultiplier),
      };
    }
  }
  if (policies.timeout !== undefined) {
    if (policies.timeout === null) delete node.timeout;
    else {
      node.timeout = {
        ...(policies.timeout.seconds > 0 ? { seconds: Math.round(policies.timeout.seconds) } : {}),
        ...(policies.timeout.onTimeout ? { on_timeout: policies.timeout.onTimeout } : {}),
      };
    }
  }
  if (policies.compensate !== undefined) {
    delete node.compensation;
    if (policies.compensate === null) delete node.compensate;
    else {
      node.compensate = {
        strategy: policies.compensate.strategy,
        ...(policies.compensate.nodeId ? { node_id: policies.compensate.nodeId } : {}),
      };
    }
  }
}
