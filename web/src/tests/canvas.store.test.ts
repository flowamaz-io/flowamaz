import { describe, it, expect, beforeEach } from 'vitest';
import { setActivePinia, createPinia } from 'pinia';
import { useCanvasStore } from '@/stores/canvas.store';
import type { CanvasNode } from '@/types/canvas.types';

const makeNode = (id: string): CanvasNode => ({
  id, type: 'action', label: `Node ${id}`, config: {}, position: { x: 0, y: 0 },
});

describe('canvas.store', () => {
  beforeEach(() => { setActivePinia(createPinia()); });

  it('adds a node', () => {
    const store = useCanvasStore();
    store.addNode(makeNode('n1'));
    expect(store.nodes).toHaveLength(1);
    expect(store.nodes[0].id).toBe('n1');
  });

  it('removes a node and its edges', () => {
    const store = useCanvasStore();
    store.addNode(makeNode('n1'));
    store.addNode(makeNode('n2'));
    store.addEdge({ id: 'e1', from: 'n1', to: 'n2' });
    store.removeNode('n1');
    expect(store.nodes).toHaveLength(1);
    expect(store.edges).toHaveLength(0);
  });

  it('sets selectedNode null when removed node was selected', () => {
    const store = useCanvasStore();
    store.addNode(makeNode('n1'));
    store.setSelectedNode('n1');
    store.removeNode('n1');
    expect(store.selectedNodeId).toBeNull();
  });

  it('isDirty becomes true after addNode', () => {
    const store = useCanvasStore();
    expect(store.isDirty).toBe(false);
    store.addNode(makeNode('n1'));
    expect(store.isDirty).toBe(true);
  });

  it('reset clears everything', () => {
    const store = useCanvasStore();
    store.addNode(makeNode('n1'));
    store.reset();
    expect(store.nodes).toHaveLength(0);
    expect(store.isDirty).toBe(false);
  });
});
