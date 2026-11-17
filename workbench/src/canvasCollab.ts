import type { Ref } from 'vue';

/**
 * A shared editing session the canvas can join: shared node positions, other people's cursors and selections, and
 * undo of this person's own changes. The Flowamaz product provides one (over Yjs); a host without live editing
 * leaves it out. Structural, so a Yjs map fits {@link SharedPositions} as it is.
 */
export interface CanvasCollab {
  /** True once this browser holds the shared document. */
  ready: Ref<boolean>;
  /** False for people who can watch but not change the document. */
  canEdit: Ref<boolean>;
  peers: Ref<CanvasCollabPeer[]>;
  positions: Ref<SharedPositions | null>;
  canUndo: Ref<boolean>;
  canRedo: Ref<boolean>;
  undo: () => void;
  redo: () => void;
  setCursor: (cursor: { x: number; y: number } | null) => void;
  setSelected: (nodeId: string | null) => void;
}

export interface CanvasCollabPeer {
  clientId: number;
  user: { userId: string; name: string; color: string };
  cursor: { x: number; y: number } | null;
  selected: string | null;
}

export type NodePosition = { x: number; y: number };

/** Node positions shared in the session (a Yjs `Y.Map<{ x, y }>` in the product). */
export interface SharedPositions {
  readonly size: number;
  get(nodeId: string): NodePosition | undefined;
  set(nodeId: string, position: NodePosition): unknown;
  forEach(callback: (position: NodePosition, nodeId: string) => void): void;
  observe(callback: (event: { transaction: { local: boolean } }) => void): void;
  unobserve(callback: (event: { transaction: { local: boolean } }) => void): void;
  /** Groups several changes into one update, when the map belongs to a document. */
  doc?: { transact(change: () => void): void } | null;
}
