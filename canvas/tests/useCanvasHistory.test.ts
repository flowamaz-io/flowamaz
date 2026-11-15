import { describe, it, expect, beforeEach } from 'vitest';
import { createCanvasModel, type CanvasModel } from '../src/model';
import { useCanvasHistory } from '../src/useCanvasHistory';

describe('useCanvasHistory', () => {
  let model: CanvasModel;
  beforeEach(() => { model = createCanvasModel(); });

  it('pushes snapshots and increments historyIndex', () => {
    const { snapshot } = useCanvasHistory(model);
    snapshot('yaml-1');
    snapshot('yaml-2');
    const store = model;
    expect(store.history).toHaveLength(2);
    expect(store.historyIndex).toBe(1);
  });

  it('undo returns previous snapshot', () => {
    const { snapshot, performUndo } = useCanvasHistory(model);
    snapshot('yaml-1');
    snapshot('yaml-2');
    const prev = performUndo();
    expect(prev).toBe('yaml-1');
  });

  it('redo returns next snapshot after undo', () => {
    const { snapshot, performUndo, performRedo } = useCanvasHistory(model);
    snapshot('yaml-1');
    snapshot('yaml-2');
    snapshot('yaml-3');
    performUndo();
    performUndo();
    const next = performRedo();
    expect(next).toBe('yaml-2');
  });

  it('undo returns null when at start', () => {
    const { snapshot, performUndo } = useCanvasHistory(model);
    snapshot('yaml-1');
    performUndo();
    const result = performUndo();
    expect(result).toBeNull();
  });

  it('5 snapshots → undo 4 times → returns first snapshot', () => {
    const { snapshot, performUndo } = useCanvasHistory(model);
    for (let i = 1; i <= 5; i++) snapshot(`yaml-${i}`);
    // Undo 3 times discarding results
    for (let i = 0; i < 3; i++) performUndo();
    // 4th undo returns yaml-1 (first snapshot)
    const result = performUndo();
    expect(result).toBe('yaml-1');
  });

  it('new action after undo truncates future history', () => {
    const { snapshot, performUndo } = useCanvasHistory(model);
    snapshot('yaml-1');
    snapshot('yaml-2');
    snapshot('yaml-3');
    performUndo();
    snapshot('yaml-new');
    const store = model;
    expect(store.history).toHaveLength(3); // yaml-1, yaml-2, yaml-new
    expect(store.history[2]).toBe('yaml-new');
  });

  it('canUndo is false initially', () => {
    const { canUndo } = useCanvasHistory(model);
    expect(canUndo()).toBe(false);
  });

  it('canUndo is true after first snapshot + another snapshot', () => {
    const { snapshot, canUndo } = useCanvasHistory(model);
    snapshot('yaml-1');
    snapshot('yaml-2');
    expect(canUndo()).toBe(true);
  });
});
