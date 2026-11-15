import * as jsYaml from 'js-yaml';
import { isMap, isScalar, isSeq, parseDocument, type Document, type YAMLMap, type YAMLSeq } from 'yaml';
import { mergeText } from './textMerge';

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

/**
 * Edit workflow YAML without rewriting it: `edit` changes a plain parsed copy, and only the parts that
 * actually changed are written back into the original document. Comments, quoting, key order and
 * flow style (`- { id: a, type: trigger }`) are kept everywhere else, so a canvas edit shows up as the
 * few lines it changed — which also keeps live editing's merges small. Falls back to a full dump when
 * the text can't be parsed as one YAML document.
 */
export function patchWorkflowYaml(text: string, edit: (doc: Record<string, unknown>) => void): string {
  let before: unknown;
  try { before = jsYaml.load(text); } catch { before = undefined; }
  const plain = (before && typeof before === 'object' && !Array.isArray(before)) ? before as Record<string, unknown> : {};
  const after = JSON.parse(JSON.stringify(plain)) as Record<string, unknown>;
  edit(after);
  if (deepEqual(plain, after)) return text;

  const doc = parseDocument(text);
  if (doc.errors.length > 0 || !isMap(doc.contents)) return dumpWorkflowYaml(after);
  // The library re-renders the whole document (normalising indentation and spacing). Render it before
  // and after the edit the same way; the difference between those two is the real change, and it is
  // merged onto the original text so every other byte stays exactly as written.
  const options = { lineWidth: 0, ...indentationOf(text) };
  const renderedBefore = doc.toString(options);
  patchNode(doc, doc.contents, plain, after);
  const renderedAfter = doc.toString(options);
  // The library always ends with a line break; compare like with like, then restore the original ending
  // (otherwise a line added at the end would be glued onto the last line).
  const endsWithBreak = text.endsWith('\n');
  const mergedWithBreak = mergeText(renderedBefore, renderedAfter, endsWithBreak ? text : `${text}\n`);
  const merged = endsWithBreak || !mergedWithBreak.endsWith('\n') ? mergedWithBreak : mergedWithBreak.slice(0, -1);
  // Safety net: the result must read back as exactly the edited document. If the original's layout
  // defeated the merge, use the library's render instead (comments kept, spacing normalised).
  try {
    if (deepEqual(jsYaml.load(merged), after)) return merged;
  } catch { /* fall through */ }
  return renderedAfter;
}

/** The document's own indentation, so rendered lines line up with it (2 or 4 spaces; lists indented or not). */
function indentationOf(text: string): { indent: number; indentSeq: boolean } {
  const lines = text.split('\n').filter((l) => l.trim() && !l.trimStart().startsWith('#'));
  const depths = lines.map((l) => l.length - l.trimStart().length).filter((d) => d > 0);
  const indent = depths.length > 0 ? Math.max(1, Math.min(...depths)) : 2;
  let indentSeq = true;
  for (let i = 0; i < lines.length - 1; i++) {
    if (!/:\s*$/.test(lines[i]) || !lines[i + 1].trimStart().startsWith('- ')) continue;
    const parent = lines[i].length - lines[i].trimStart().length;
    const child = lines[i + 1].length - lines[i + 1].trimStart().length;
    indentSeq = child > parent;
    break;
  }
  return { indent, indentSeq };
}

function patchNode(doc: Document, node: YAMLMap | YAMLSeq, before: unknown, after: unknown): void {
  if (isMap(node) && isPlainObject(before) && isPlainObject(after)) {
    for (const key of Object.keys(before)) {
      if (!(key in after)) node.delete(key);
    }
    for (const [key, value] of Object.entries(after)) {
      const prev = before[key];
      if (key in before && deepEqual(prev, value)) continue;
      const child = node.get(key, true);
      if ((isMap(child) || isSeq(child)) && key in before && sameShape(prev, value)) {
        patchNode(doc, child, prev, value);
      } else if (isScalar(child) && typeof value === 'string' && (child.type === 'QUOTE_DOUBLE' || child.type === 'QUOTE_SINGLE')) {
        // A quoted value stays quoted the same way ("Review" → "Review (Olivia)").
        child.value = value;
      } else {
        node.set(key, createLike(doc, value, node));
      }
    }
    return;
  }
  if (isSeq(node) && Array.isArray(before) && Array.isArray(after)) {
    const byId = idsOf(before);
    if (byId && idsOf(after)) {
      // Lists of things with ids (nodes, edges): reuse each item's YAML (with its comments) by id.
      const items = node.items;
      node.items = after.map((value) => {
        const idx = byId.get(itemKey(value));
        if (idx === undefined) return createLike(doc, value, node);
        const item = items[idx];
        if (!deepEqual(before[idx], value)) {
          if (isMap(item) && isPlainObject(value)) patchNode(doc, item, before[idx], value);
          else return createLike(doc, value, node);
        }
        return item;
      });
      return;
    }
    if (before.length === after.length) {
      after.forEach((value, idx) => {
        if (deepEqual(before[idx], value)) return;
        const item = node.items[idx];
        if ((isMap(item) || isSeq(item)) && sameShape(before[idx], value)) patchNode(doc, item, before[idx], value);
        else node.items[idx] = createLike(doc, value, node);
      });
      return;
    }
    node.items = after.map((value) => createLike(doc, value, node));
  }
}

/** A new YAML node in the same style as its siblings (flow `{ … }` items stay flow). */
function createLike(doc: Document, value: unknown, parent: YAMLMap | YAMLSeq): ReturnType<Document['createNode']> {
  const created = doc.createNode(value);
  const siblingFlow = isSeq(parent) ? parent.items.some((i) => (isMap(i) || isSeq(i)) && i.flow) : parent.flow;
  if (siblingFlow && (isMap(created) || isSeq(created))) created.flow = true;
  return created;
}

/** Identity of a list item: its id, or for edges without one, from → to (+ via). */
function itemKey(item: unknown): unknown {
  if (!isPlainObject(item)) return undefined;
  if (item.id !== undefined) return `id:${String(item.id)}`;
  if (item.from !== undefined && item.to !== undefined) return `edge:${String(item.from)}>${String(item.to)}>${String(item.via ?? '')}`;
  return undefined;
}

function idsOf(list: unknown[]): Map<unknown, number> | null {
  const map = new Map<unknown, number>();
  for (let i = 0; i < list.length; i++) {
    const key = itemKey(list[i]);
    if (key === undefined || map.has(key)) return null;
    map.set(key, i);
  }
  return map;
}

function sameShape(a: unknown, b: unknown): boolean {
  return (Array.isArray(a) && Array.isArray(b)) || (isPlainObject(a) && isPlainObject(b));
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function deepEqual(a: unknown, b: unknown): boolean {
  if (a === b) return true;
  if (typeof a !== typeof b || a === null || b === null || typeof a !== 'object') return false;
  if (Array.isArray(a) !== Array.isArray(b)) return false;
  if (Array.isArray(a)) {
    const bb = b as unknown[];
    return a.length === bb.length && a.every((v, i) => deepEqual(v, bb[i]));
  }
  const ka = Object.keys(a as object);
  const kb = Object.keys(b as object);
  return ka.length === kb.length && ka.every((k) => deepEqual((a as Record<string, unknown>)[k], (b as Record<string, unknown>)[k]));
}
