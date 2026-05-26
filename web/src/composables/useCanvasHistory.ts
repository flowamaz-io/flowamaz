import { useCanvasStore } from '@/stores/canvas.store';

export function useCanvasHistory() {
  const store = useCanvasStore();

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
