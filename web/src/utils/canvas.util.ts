/**
 * Pure helpers for the SFG canvas: grid snapping, minimap coordinate mapping and
 * locating a node's definition line in workflow YAML. Kept free of Vue/Cytoscape so
 * they can be unit tested in isolation.
 */

// ── Grid snap ────────────────────────────────────────────────────────────────

export const GRID_SIZE = 20;
export const SNAP_PREF_KEY = 'fmz_canvas_snap_to_grid';

export interface Point { x: number; y: number }

/** Round a single coordinate to the nearest multiple of `grid`. */
export function snapToGrid(value: number, grid = GRID_SIZE): number {
  if (!Number.isFinite(value) || grid <= 0) return value;
  const snapped = Math.round(value / grid) * grid;
  // Avoid returning -0, which renders oddly in YAML/debug output.
  return snapped === 0 ? 0 : snapped;
}

export function snapPosition(pos: Point, grid = GRID_SIZE): Point {
  return { x: snapToGrid(pos.x, grid), y: snapToGrid(pos.y, grid) };
}

type StorageLike = Pick<Storage, 'getItem' | 'setItem'>;

function defaultStorage(): StorageLike | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage;
  } catch {
    return null;
  }
}

/** Read the per-viewer snap preference. Any storage failure falls back to "off". */
export function loadSnapPreference(storage: StorageLike | null = defaultStorage()): boolean {
  try {
    return storage?.getItem(SNAP_PREF_KEY) === 'true';
  } catch {
    return false;
  }
}

/** Persist the per-viewer snap preference. Storage failures are ignored (private mode etc). */
export function saveSnapPreference(enabled: boolean, storage: StorageLike | null = defaultStorage()): void {
  try {
    storage?.setItem(SNAP_PREF_KEY, String(enabled));
  } catch { /* preference simply won't persist */ }
}

// ── Minimap mapping ──────────────────────────────────────────────────────────

export interface ModelBox { x1: number; y1: number; w: number; h: number }

export interface MinimapTransform {
  /** Minimap pixels per model unit. */
  scale: number;
  offsetX: number;
  offsetY: number;
  bb: ModelBox;
}

export interface MinimapRect { left: number; top: number; width: number; height: number }

/**
 * Fit the model bounding box into a minimap of `width` x `height` pixels, centred,
 * preserving aspect ratio (the same geometry as CSS `object-fit: contain`).
 */
export function computeMinimapTransform(bb: ModelBox, width: number, height: number): MinimapTransform {
  const bw = bb.w > 0 ? bb.w : 1;
  const bh = bb.h > 0 ? bb.h : 1;
  const scale = Math.min(width / bw, height / bh);
  return {
    scale,
    offsetX: (width - bw * scale) / 2,
    offsetY: (height - bh * scale) / 2,
    bb: { x1: bb.x1, y1: bb.y1, w: bw, h: bh },
  };
}

export function modelToMinimap(p: Point, t: MinimapTransform): Point {
  return {
    x: t.offsetX + (p.x - t.bb.x1) * t.scale,
    y: t.offsetY + (p.y - t.bb.y1) * t.scale,
  };
}

export function minimapToModel(p: Point, t: MinimapTransform): Point {
  const scale = t.scale > 0 ? t.scale : 1;
  return {
    x: t.bb.x1 + (p.x - t.offsetX) / scale,
    y: t.bb.y1 + (p.y - t.offsetY) / scale,
  };
}

/** Project the visible model extent (cy.extent()) onto the minimap as a rectangle. */
export function viewportToMinimapRect(extent: ModelBox, t: MinimapTransform): MinimapRect {
  const topLeft = modelToMinimap({ x: extent.x1, y: extent.y1 }, t);
  return { left: topLeft.x, top: topLeft.y, width: extent.w * t.scale, height: extent.h * t.scale };
}

/** Pan value that puts `model` at the centre of a viewport of the given rendered size. */
export function panToCenter(model: Point, zoom: number, viewWidth: number, viewHeight: number): Point {
  return { x: viewWidth / 2 - model.x * zoom, y: viewHeight / 2 - model.y * zoom };
}

/** Convert a rendered (container-relative) point to model coordinates. */
export function renderedToModel(rendered: Point, pan: Point, zoom: number): Point {
  const z = zoom > 0 ? zoom : 1;
  return { x: (rendered.x - pan.x) / z, y: (rendered.y - pan.y) / z };
}

// ── YAML node line lookup ────────────────────────────────────────────────────

function indentOf(line: string): number {
  return line.length - line.trimStart().length;
}

function isBlankOrComment(line: string): boolean {
  const t = line.trim();
  return t === '' || t.startsWith('#');
}

/** Parse a YAML scalar at the start of `text` (quoted or plain); in flow context stop at `,}]`. */
function readScalar(text: string, flow: boolean): string | null {
  const s = text.trimStart();
  if (s.startsWith('"')) {
    let out = '';
    for (let i = 1; i < s.length; i++) {
      const c = s[i];
      if (c === '\\' && i + 1 < s.length) { out += s[i + 1]; i++; continue; }
      if (c === '"') return out;
      out += c;
    }
    return null;
  }
  if (s.startsWith("'")) {
    let out = '';
    for (let i = 1; i < s.length; i++) {
      const c = s[i];
      if (c === "'") {
        if (s[i + 1] === "'") { out += "'"; i++; continue; }
        return out;
      }
      out += c;
    }
    return null;
  }
  const stop = flow ? /\s+#|[,}\]]/ : /\s+#/;
  const m = stop.exec(s);
  const raw = (m ? s.slice(0, m.index) : s).trim();
  return raw === '' ? null : raw;
}

/** Split a line into its top-level `{...}` flow mappings (quotes respected). */
function splitFlowMaps(line: string): string[] {
  const maps: string[] = [];
  let depth = 0;
  let quote: string | null = null;
  let startIdx = -1;
  for (let i = 0; i < line.length; i++) {
    const c = line[i];
    if (quote) {
      if (c === quote) quote = null;
      else if (c === '\\' && quote === '"') i++;
      continue;
    }
    if (c === '"' || c === "'") { quote = c; continue; }
    if (c === '#' && depth === 0 && (i === 0 || /\s/.test(line[i - 1]))) break;
    if (c === '{') { if (depth === 0) startIdx = i; depth++; continue; }
    if (c === '}') {
      depth--;
      if (depth === 0 && startIdx >= 0) { maps.push(line.slice(startIdx, i + 1)); startIdx = -1; }
    }
  }
  return maps;
}

/**
 * Drop everything nested deeper than the outermost flow mapping so only its own keys
 * remain, e.g. `{ config: { id: z }, id: x }` → `{ config: , id: x }`. Quotes are respected.
 */
function stripNestedFlow(text: string): string {
  let depth = 0;
  let quote: string | null = null;
  let out = '';
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (quote) {
      if (depth <= 1) out += c;
      if (c === '\\' && quote === '"' && i + 1 < text.length) {
        if (depth <= 1) out += text[i + 1];
        i++;
      } else if (c === quote) {
        quote = null;
      }
      continue;
    }
    if (c === '"' || c === "'") { quote = c; if (depth <= 1) out += c; continue; }
    if (c === '{' || c === '[') { depth++; if (depth <= 1) out += c; continue; }
    if (c === '}' || c === ']') { if (depth <= 1) out += c; depth--; continue; }
    if (depth <= 1) out += c;
  }
  return out;
}

/** Return the id defined by a flow mapping such as `{ id: x, type: y }`. */
function flowMapId(flowText: string): string | null {
  const flat = stripNestedFlow(flowText);
  const re = /(?:^\{|,)\s*(?:id|"id"|'id')\s*:\s*/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(flat)) !== null) {
    const value = readScalar(flat.slice(m.index + m[0].length), true);
    if (value !== null) return value;
  }
  return null;
}

const ID_KEY = /^(?:id|"id"|'id')\s*:(?:\s+|$)/;

function idFromBlockKey(text: string): string | null {
  if (!ID_KEY.test(text)) return null;
  return readScalar(text.slice(text.indexOf(':') + 1), false);
}

interface NodesBlock { start: number; end: number; flow: boolean }

/** Locate the [start, end) line range of the `nodes:` block directly under top-level `spec:`. */
function findSpecNodesBlock(lines: string[]): NodesBlock | null {
  let inSpec = false;
  let specChildIndent = -1;
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (isBlankOrComment(line)) continue;
    const ind = indentOf(line);
    if (ind === 0) {
      inSpec = /^spec\s*:\s*(#.*)?$/.test(line);
      specChildIndent = -1;
      continue;
    }
    if (!inSpec) continue;
    if (specChildIndent < 0) specChildIndent = ind;
    if (ind !== specChildIndent) continue;
    const m = /^\s*nodes\s*:(.*)$/.exec(line);
    if (!m) continue;

    // Flow sequence (`nodes: [ ... ]`) — the block runs until the brackets close.
    if (m[1].trim().startsWith('[')) {
      let depth = 0;
      let end = i + 1;
      for (let j = i; j < lines.length; j++) {
        const text = j === i ? m[1] : lines[j];
        for (const c of text) { if (c === '[') depth++; else if (c === ']') depth--; }
        end = j + 1;
        if (depth <= 0) break;
      }
      return { start: i, end, flow: true };
    }

    let end = i + 1;
    for (let j = i + 1; j < lines.length; j++) {
      const l = lines[j];
      if (isBlankOrComment(l)) continue;
      const li = indentOf(l);
      // A block sequence may sit at the same indent as `nodes:` (compact style).
      if (li < ind || (li === ind && !l.trimStart().startsWith('-'))) break;
      end = j + 1;
    }
    return { start: i + 1, end, flow: false };
  }
  return null;
}

/**
 * Scan node items. Only list items at the node-list indent (the first `-` seen) count,
 * so nested lists/maps inside a node's config never produce false matches.
 */
function scanForNodeId(lines: string[], block: NodesBlock, nodeId: string): number | null {
  if (block.flow) {
    for (let i = block.start; i < block.end; i++) {
      for (const map of splitFlowMaps(lines[i])) {
        if (flowMapId(map) === nodeId) return i + 1;
      }
    }
    return null;
  }
  let itemIndent = -1;
  let itemKeyIndent = -1;
  for (let i = block.start; i < block.end; i++) {
    const line = lines[i];
    if (isBlankOrComment(line)) continue;
    const ind = indentOf(line);
    const item = /^(\s*)-(\s+)(.*)$/.exec(line);
    if (item && (itemIndent < 0 || ind === itemIndent)) {
      itemIndent = ind;
      itemKeyIndent = ind + 1 + item[2].length;
      const rest = item[3];
      if (rest.startsWith('{')) {
        const map = splitFlowMaps(rest)[0];
        if (map && flowMapId(map) === nodeId) return i + 1;
        continue;
      }
      if (idFromBlockKey(rest) === nodeId) return i + 1;
      continue;
    }
    if (itemKeyIndent >= 0 && ind === itemKeyIndent && idFromBlockKey(line.trimStart()) === nodeId) {
      return i + 1;
    }
  }
  return null;
}

/**
 * Find the 1-based line where `nodeId` is defined as a node in workflow YAML.
 * Only `spec.nodes` is searched, so edge/metadata ids never match. Handles `- id: x`,
 * `id: x` as a later key of a list item, flow style `- { id: x, ... }` /
 * `nodes: [{ id: x }]`, and single/double-quoted ids. Returns null when not found.
 */
export function findNodeIdLine(yaml: string, nodeId: string): number | null {
  if (!yaml || !nodeId) return null;
  const lines = yaml.split(/\r?\n/);
  const block = findSpecNodesBlock(lines);
  return block ? scanForNodeId(lines, block, nodeId) : null;
}

// ── Grid background ──────────────────────────────────────────────────────────

/**
 * Rendered spacing for the dot grid at a given zoom. Always a power-of-two multiple of
 * `grid` so every dot is a valid snap target, doubled until dots are at least `minPx` apart.
 */
export function gridDotSpacing(zoom: number, grid = GRID_SIZE, minPx = 10): number {
  const z = zoom > 0 && Number.isFinite(zoom) ? zoom : 1;
  let step = grid * z;
  let guard = 0;
  while (step < minPx && guard < 16) { step *= 2; guard++; }
  return step;
}
