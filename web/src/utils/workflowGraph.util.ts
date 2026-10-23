import * as jsYaml from 'js-yaml';
import type { CanvasEdge, CanvasNode } from '@/types/canvas.types';

type Dict = Record<string, unknown>;

const isDict = (v: unknown): v is Dict => typeof v === 'object' && v !== null && !Array.isArray(v);
const str = (v: unknown): string | undefined =>
  typeof v === 'string' ? v : typeof v === 'number' || typeof v === 'boolean' ? String(v) : undefined;

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

  const spec = isDict(doc.spec) ? doc.spec : doc;
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
      type: (str(raw.type)?.trim() || 'action') as CanvasNode['type'],
      label: str(raw.label)?.trim() || id,
      config: isDict(raw.config) ? raw.config : {},
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
