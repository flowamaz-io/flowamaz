import * as Y from 'yjs';
import { textChanges } from './textDiff';

/**
 * Three-way text merge: the changes from `base` to `mine` and from `base` to `theirs`, combined.
 * Uses the same CRDT as live editing, so the result is what live editing would have produced had
 * both sets of changes been made at the same moment: edits in different places both survive, and
 * neither side's unchanged text is duplicated or lost.
 *
 * Used when an edit was built from an out-of-date copy (a canvas edit started before a collaborator's
 * change arrived) and when someone rejoins with changes made while disconnected.
 */
export function mergeText(base: string, mine: string, theirs: string): string {
  if (mine === base || mine === theirs) return theirs;
  if (theirs === base) return mine;

  const origin = new Y.Doc();
  origin.getText('t').insert(0, base);
  const baseState = Y.encodeStateAsUpdate(origin);
  origin.destroy();

  const fork = (target: string): Y.Doc => {
    const d = new Y.Doc();
    Y.applyUpdate(d, baseState);
    const t = d.getText('t');
    const changes = textChanges(base, target);
    d.transact(() => {
      for (let k = changes.length - 1; k >= 0; k--) {
        const c = changes[k];
        if (c.deleteCount > 0) t.delete(c.index, c.deleteCount);
        if (c.insert) t.insert(c.index, c.insert);
      }
    });
    return d;
  };

  const a = fork(mine);
  const b = fork(theirs);
  Y.applyUpdate(a, Y.encodeStateAsUpdate(b));
  const merged = a.getText('t').toString();
  a.destroy();
  b.destroy();
  return merged;
}
