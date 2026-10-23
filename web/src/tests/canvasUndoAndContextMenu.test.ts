import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { setActivePinia, createPinia } from 'pinia';
import { mount } from '@vue/test-utils';
import cytoscape, { type Core } from 'cytoscape';
import { useCanvasStore } from '@/stores/canvas.store';
import { useYamlCanvasSync } from '@/composables/useYamlCanvasSync';
import { nodeIdAtRenderedPoint } from '@/composables/useCytoscapeCanvas';
import FmYamlEditor from '@/components/editor/FmYamlEditor.vue';

const LOADED = `apiVersion: flowamaz/v1
kind: Workflow
metadata: { id: wf, name: WF }
spec:
  nodes:
    - { id: a, type: action, label: A }
    - { id: b, type: action, label: B }
  edges:
    - { id: e1, from: a, to: b }
`;

function headlessCy(): Core {
  return cytoscape({
    headless: true,
    styleEnabled: true,
    style: [{ selector: 'node', style: { width: 100, height: 40 } }],
  });
}

describe('canvas undo history (B3)', () => {
  beforeEach(() => { setActivePinia(createPinia()); });

  it('the first edit after load is undoable once the loaded document is the baseline', () => {
    const store = useCanvasStore();
    store.resetHistory(LOADED);
    expect(store.canUndo).toBe(false);
    expect(store.undoCount).toBe(0);

    store.pushHistory('after duplicate');
    expect(store.undoCount).toBe(1);
    expect(store.undo()).toBe(LOADED);
    expect(store.canUndo).toBe(false);
    expect(store.redo()).toBe('after duplicate');
  });

  it('resetHistory drops history left over from another workflow', () => {
    const store = useCanvasStore();
    store.pushHistory('other workflow v1');
    store.pushHistory('other workflow v2');
    store.resetHistory(LOADED);
    expect(store.history).toEqual([LOADED]);
    expect(store.undo()).toBeNull();
  });
});

describe('useYamlCanvasSync force re-sync (B3)', () => {
  let cy: Core;
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.useFakeTimers();
    cy = headlessCy();
  });
  afterEach(() => {
    vi.useRealTimers();
    cy.destroy();
  });

  async function settle() {
    await vi.advanceTimersByTimeAsync(400);
  }

  it('undo back to the loaded document removes duplicated nodes and restores deleted ones in place', async () => {
    const { syncYamlToCanvas, rememberPosition } = useYamlCanvasSync(() => cy);
    cy.on('remove', 'node', (evt) => rememberPosition(evt.target.id(), evt.target.position()));

    void syncYamlToCanvas(LOADED);
    await settle();
    expect(cy.nodes().map((n) => n.id()).sort()).toEqual(['a', 'b']);

    // Duplicate (canvas-only change, as Ctrl/Cmd+D does), then undo to the loaded document.
    cy.add({ group: 'nodes', data: { id: 'action-3', label: 'A (copy)', nodeType: 'action' } });
    void syncYamlToCanvas(LOADED);
    await settle();
    expect(cy.getElementById('action-3').length).toBe(1); // unforced: same doc → skipped

    void syncYamlToCanvas(LOADED, { force: true });
    await settle();
    expect(cy.nodes().map((n) => n.id()).sort()).toEqual(['a', 'b']);

    // Delete b on the canvas, then undo: it comes back where it was.
    cy.getElementById('b').position({ x: 321, y: 123 });
    cy.getElementById('b').remove();
    void syncYamlToCanvas(LOADED, { force: true });
    await settle();
    expect(cy.getElementById('b').position()).toEqual({ x: 321, y: 123 });
    expect(cy.getElementById('e1').length).toBe(1);
  });
});

describe('nodeIdAtRenderedPoint (B4 right-click target)', () => {
  it('returns the node under the cursor, or null on empty canvas', () => {
    const cy = headlessCy();
    cy.add([
      { group: 'nodes', data: { id: 'left' }, position: { x: 100, y: 100 } },
      { group: 'nodes', data: { id: 'right' }, position: { x: 400, y: 100 } },
    ]);
    cy.getElementById('left').select();

    expect(nodeIdAtRenderedPoint(cy, 400, 100)).toBe('right');
    expect(nodeIdAtRenderedPoint(cy, 110, 95)).toBe('left');
    expect(nodeIdAtRenderedPoint(cy, 250, 300)).toBeNull();

    // Follows pan/zoom (rendered coordinates).
    cy.zoom(2);
    cy.pan({ x: 0, y: 0 });
    expect(nodeIdAtRenderedPoint(cy, 800, 200)).toBe('right');
    cy.destroy();
  });

  it('ignores group boxes', () => {
    const cy = headlessCy();
    cy.add({ group: 'nodes', data: { id: 'group-1', isGroup: true }, position: { x: 0, y: 0 } });
    expect(nodeIdAtRenderedPoint(cy, 0, 0)).toBeNull();
    cy.destroy();
  });
});

describe('FmYamlEditor diagnostics refresh (O2)', () => {
  it('shows markers when the diagnostics prop changes and drops them when the document is replaced', async () => {
    const doc = 'a: 1\nb: 2\nc: 3\n';
    const wrapper = mount(FmYamlEditor, { props: { modelValue: doc, diagnostics: [] } });
    const vm = wrapper.vm as unknown as { shownDiagnosticCount: () => number };
    expect(vm.shownDiagnosticCount()).toBe(0);

    await wrapper.setProps({ diagnostics: [{ from: 5, to: 9, message: 'Unknown node', severity: 'error' }] });
    expect(vm.shownDiagnosticCount()).toBe(1);

    // Canvas deleted the offending node: the document is replaced → stale marker is gone at once.
    await wrapper.setProps({ modelValue: 'a: 1\n' });
    expect(vm.shownDiagnosticCount()).toBe(0);

    // Validation of the new document comes back clean.
    await wrapper.setProps({ diagnostics: [] });
    expect(vm.shownDiagnosticCount()).toBe(0);

    // Out-of-range offsets from a late response are clamped, not thrown.
    await wrapper.setProps({ diagnostics: [{ from: 50, to: 90, message: 'late', severity: 'warning' }] });
    expect(vm.shownDiagnosticCount()).toBe(1);
  });
});
