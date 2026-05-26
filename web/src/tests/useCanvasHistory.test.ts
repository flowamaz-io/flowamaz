import { describe, it, expect, beforeEach } from 'vitest';
import { setActivePinia, createPinia } from 'pinia';
import { useCanvasStore } from '@/stores/canvas.store';
import { useCanvasHistory } from '@/composables/useCanvasHistory';

describe('useCanvasHistory', () => {
  beforeEach(() => { setActivePinia(createPinia()); });

  it('pushes snapshots and increments historyIndex', () => {
    const { snapshot } = useCanvasHistory();
    snapshot('yaml-1');
    snapshot('yaml-2');
    const store = useCanvasStore();
    expect(store.history).toHaveLength(2);
    expect(store.historyIndex).toBe(1);
  });

  it('undo returns previous snapshot', () => {
    const { snapshot, performUndo } = useCanvasHistory();
    snapshot('yaml-1');
    snapshot('yaml-2');
    const prev = performUndo();
    expect(prev).toBe('yaml-1');
  });

  it('redo returns next snapshot after undo', () => {
    const { snapshot, performUndo, performRedo } = useCanvasHistory();
    snapshot('yaml-1');
    snapshot('yaml-2');
    snapshot('yaml-3');
    performUndo();
    performUndo();
    const next = performRedo();
    expect(next).toBe('yaml-2');
  });

  it('undo returns null when at start', () => {
    const { snapshot, performUndo } = useCanvasHistory();
    snapshot('yaml-1');
    performUndo();
    const result = performUndo();
    expect(result).toBeNull();
  });

  it('5 snapshots → undo 4 times → returns first snapshot', () => {
    const { snapshot, performUndo } = useCanvasHistory();
    for (let i = 1; i <= 5; i++) snapshot(`yaml-${i}`);
    // Undo 3 times discarding results
    for (let i = 0; i < 3; i++) performUndo();
    // 4th undo returns yaml-1 (first snapshot)
    const result = performUndo();
    expect(result).toBe('yaml-1');
  });

  it('new action after undo truncates future history', () => {
    const { snapshot, performUndo } = useCanvasHistory();
    snapshot('yaml-1');
    snapshot('yaml-2');
    snapshot('yaml-3');
    performUndo();
    snapshot('yaml-new');
    const store = useCanvasStore();
    expect(store.history).toHaveLength(3); // yaml-1, yaml-2, yaml-new
    expect(store.history[2]).toBe('yaml-new');
  });

  it('canUndo is false initially', () => {
    const { canUndo } = useCanvasHistory();
    expect(canUndo()).toBe(false);
  });

  it('canUndo is true after first snapshot + another snapshot', () => {
    const { snapshot, canUndo } = useCanvasHistory();
    snapshot('yaml-1');
    snapshot('yaml-2');
    expect(canUndo()).toBe(true);
  });
});
