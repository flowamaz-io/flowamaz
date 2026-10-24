import * as jsYaml from 'js-yaml';

/**
 * The one way the web app serialises workflow YAML.
 *
 * `noRefs: true` stops js-yaml from emitting anchors/aliases (`&ref_0` / `*ref_0`) when the same
 * object or array instance appears twice in the document — workflow YAML must stay plain so users
 * can read and edit it, and so a later edit to one copy never silently changes the other.
 */
export function dumpWorkflowYaml(doc: unknown, options: jsYaml.DumpOptions = {}): string {
  return jsYaml.dump(doc, { indent: 2, lineWidth: -1, ...options, noRefs: true });
}

/** A blank flowamaz/v1 workflow for the canvas. Every collection is a fresh instance. */
export function buildCanvasSkeleton(name: string): Record<string, unknown> {
  return {
    apiVersion: 'flowamaz/v1',
    kind: 'Workflow',
    metadata: {
      id: name.toLowerCase().replace(/\s+/g, '-').replace(/[^a-z0-9-]/g, ''),
      name,
    },
    spec: { trigger: { type: 'manual' }, nodes: [], edges: [] },
  };
}

/** True when some object/array instance is reachable twice (i.e. the YAML used an alias). */
function hasSharedInstances(value: unknown, seen = new Set<object>()): boolean {
  if (typeof value !== 'object' || value === null) return false;
  if (seen.has(value)) return true;
  seen.add(value);
  const children = Array.isArray(value) ? value : Object.values(value as Record<string, unknown>);
  return children.some((child) => hasSharedInstances(child, seen));
}

/**
 * Expand YAML anchors/aliases into plain copies. A document that uses an alias (e.g. a server
 * skeleton serialised as `nodes: &o0 []` / `edges: *o0`) parses to ONE shared array, so adding a
 * node would also add it to `edges`. Documents without aliases are returned byte-for-byte as given
 * (comments and formatting kept); unparseable text is returned unchanged for the validator to report.
 */
export function expandYamlAliases(text: string): string {
  if (!/[&*][^\s]/.test(text)) return text;
  let doc: unknown;
  try { doc = jsYaml.load(text); } catch { return text; }
  return hasSharedInstances(doc) ? dumpWorkflowYaml(doc) : text;
}
