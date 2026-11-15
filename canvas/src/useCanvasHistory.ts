import type { CanvasModel } from './model';

/** Undo/redo over the canvas model's YAML history. */
export function useCanvasHistory(store: CanvasModel) {

  function snapshot(yaml: string) {
    store.pushHistory(yaml);
  }

  function performUndo(): string | null {
    return store.undo();
  }

  function performRedo(): string | null {
    return store.redo();
  }

  return {
    snapshot,
    performUndo,
    performRedo,
    canUndo: () => store.canUndo,
    canRedo: () => store.canRedo,
    undoCount: () => store.undoCount,
  };
}
