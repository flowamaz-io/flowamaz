/**
 * Text diffs for live editing. A change elsewhere in the document (a canvas edit, a collaborator,
 * Co-pilot) is applied as a few small edits rather than one big replacement, so:
 * - the YAML editor's cursor stays put, and
 * - in the shared document, unchanged lines are never deleted and re-inserted. Re-inserting a line
 *   someone else edited at the same time would bring back their old text next to their new text.
 */
export interface TextChange {
  /** Offset in the *original* text. */
  index: number;
  deleteCount: number;
  insert: string;
}

/** The smallest single edit that turns `before` into `after`: keep the common start and end, replace the middle. */
export function minimalTextChange(before: string, after: string): TextChange {
  if (before === after) return { index: 0, deleteCount: 0, insert: '' };
  const max = Math.min(before.length, after.length);
  let start = 0;
  while (start < max && before.charCodeAt(start) === after.charCodeAt(start)) start++;
  let endBefore = before.length;
  let endAfter = after.length;
  while (endBefore > start && endAfter > start && before.charCodeAt(endBefore - 1) === after.charCodeAt(endAfter - 1)) {
    endBefore--;
    endAfter--;
  }
  // Don't split a surrogate pair (emoji in a label) between the kept and replaced parts.
  if (start > 0 && isHighSurrogate(before.charCodeAt(start - 1))) start--;
  if (endBefore < before.length && isLowSurrogate(before.charCodeAt(endBefore)) && endBefore > start) {
    endBefore++;
    endAfter++;
  }
  return { index: start, deleteCount: endBefore - start, insert: after.slice(start, endAfter) };
}

/** Above this many line comparisons, fall back to one span (very large documents only). */
const MAX_LINE_COMPARISONS = 4_000_000;

/**
 * Edits that turn `before` into `after`, line by line (longest common subsequence of lines), each
 * narrowed to the characters that changed. Sorted by `index`, non-overlapping, offsets in `before`.
 */
export function textChanges(before: string, after: string): TextChange[] {
  if (before === after) return [];
  const a = splitLines(before);
  const b = splitLines(after);
  // Common leading and trailing lines need no comparison.
  let head = 0;
  while (head < a.length && head < b.length && a[head] === b[head]) head++;
  let tailA = a.length;
  let tailB = b.length;
  while (tailA > head && tailB > head && a[tailA - 1] === b[tailB - 1]) { tailA--; tailB--; }
  const midA = a.slice(head, tailA);
  const midB = b.slice(head, tailB);
  const offset = sumLengths(a, 0, head);

  if (midA.length * midB.length > MAX_LINE_COMPARISONS) {
    const change = minimalTextChange(before, after);
    return change.deleteCount === 0 && change.insert === '' ? [] : [change];
  }

  // LCS table over the middle lines.
  const n = midA.length;
  const m = midB.length;
  const lcs: Uint32Array[] = Array.from({ length: n + 1 }, () => new Uint32Array(m + 1));
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      lcs[i][j] = midA[i] === midB[j] ? lcs[i + 1][j + 1] + 1 : Math.max(lcs[i + 1][j], lcs[i][j + 1]);
    }
  }

  // Walk it, grouping runs of removed/added lines into one change each.
  const changes: TextChange[] = [];
  let i = 0;
  let j = 0;
  let pos = offset;
  while (i < n || j < m) {
    if (i < n && j < m && midA[i] === midB[j]) {
      pos += midA[i].length;
      i++;
      j++;
      continue;
    }
    const startPos = pos;
    let removed = '';
    let added = '';
    while (i < n || j < m) {
      if (i < n && j < m && midA[i] === midB[j]) break;
      if (j < m && (i >= n || lcs[i][j + 1] >= lcs[i + 1][j])) { added += midB[j]; j++; }
      else { removed += midA[i]; pos += midA[i].length; i++; }
    }
    // Narrow to the characters that differ (typing one letter changes one letter, not the line).
    const inner = minimalTextChange(removed, added);
    if (inner.deleteCount > 0 || inner.insert) {
      changes.push({ index: startPos + inner.index, deleteCount: inner.deleteCount, insert: inner.insert });
    }
  }
  return changes;
}

/** Apply changes (offsets in the original text) to a string. */
export function applyTextChanges(text: string, changes: TextChange[]): string {
  let out = text;
  for (let k = changes.length - 1; k >= 0; k--) {
    const c = changes[k];
    out = out.slice(0, c.index) + c.insert + out.slice(c.index + c.deleteCount);
  }
  return out;
}

/** Lines with their line breaks attached, so joining them gives back the text exactly. */
function splitLines(text: string): string[] {
  const lines = text.match(/[^\n]*\n|[^\n]+$/g);
  return lines ?? [];
}

function sumLengths(lines: string[], from: number, to: number): number {
  let total = 0;
  for (let k = from; k < to; k++) total += lines[k].length;
  return total;
}

function isHighSurrogate(code: number): boolean {
  return code >= 0xd800 && code <= 0xdbff;
}

function isLowSurrogate(code: number): boolean {
  return code >= 0xdc00 && code <= 0xdfff;
}
