import { ref, onMounted, onUnmounted } from 'vue';
import type { Ref } from 'vue';
import cytoscape from 'cytoscape';
import type { Core, NodeSingular, CytoscapeOptions } from 'cytoscape';
import edgehandles from 'cytoscape-edgehandles';
import dagre from 'cytoscape-dagre';
import { NODE_VISUALS } from '@/types/canvas.types';
import type { NodeType } from '@/types/canvas.types';
import { useCanvasStore } from '@/stores/canvas.store';
import { snapPosition } from '@/utils/canvas.util';

// Register extensions once
// eslint-disable-next-line @typescript-eslint/no-explicit-any
cytoscape.use(edgehandles as any);
// eslint-disable-next-line @typescript-eslint/no-explicit-any
cytoscape.use(dagre as any);

function buildStylesheet(): CytoscapeOptions['style'] {
  const styles: CytoscapeOptions['style'] = [
    {
      selector: 'node',
      style: {
        'label': 'data(label)',
        'text-valign': 'center',
        'text-halign': 'center',
        'font-size': '12px',
        'font-family': 'Inter, sans-serif',
        'color': '#ffffff',
        'width': 160,
        'height': 48,
        'background-color': '#374151',
        'border-width': 2,
        'border-color': '#6B7280',
        'text-wrap': 'wrap',
        'text-max-width': '140px',
      },
    },
    {
      selector: 'node:selected',
      style: {
        'border-color': '#1D9E75',
        'border-width': 3,
      },
    },
    {
      selector: 'edge',
      style: {
        'width': 2,
        'line-color': '#94A3B8',
        'target-arrow-color': '#94A3B8',
        'target-arrow-shape': 'triangle',
        'curve-style': 'bezier',
        'arrow-scale': 1.2,
      },
    },
    {
      selector: 'edge[label]',
      style: {
        'label': 'data(label)',
        'font-size': '10px',
        'color': '#64748B',
        'text-rotation': 'autorotate',
        'text-background-color': '#ffffff',
        'text-background-opacity': 0.8,
        'text-background-padding': '2px',
      },
    },
    {
      selector: 'edge:selected',
      style: {
        'line-color': '#818CF8',
        'target-arrow-color': '#818CF8',
      },
    },
    {
      selector: '.annotation',
      style: {
        'background-color': '#FEF9C3',
        'border-color': '#FBBF24',
        'border-style': 'dashed',
        'color': '#78350F',
        'font-style': 'italic',
      },
    },
    {
      selector: ':parent',
      style: {
        'background-opacity': 0.1,
        'border-width': 2,
        'border-color': '#6B7280',
        'padding': '20px',
        'label': 'data(label)',
        'text-valign': 'top',
        'text-halign': 'center',
        'color': '#374151',
        'font-size': '11px',
      },
    },
    {
      selector: '.eh-handle',
      style: {
        'background-color': '#818CF8',
        'width': 12,
        'height': 12,
        'shape': 'ellipse',
        'overlay-opacity': 0,
        'border-width': 2,
        'border-opacity': 0,
      },
    },
    {
      selector: '.eh-ghost-edge',
      style: {
        'background-color': '#818CF8',
        'line-color': '#818CF8',
        'target-arrow-color': '#818CF8',
        'source-arrow-color': '#818CF8',
      },
    },
  ];

  // Per-type styles
  (Object.keys(NODE_VISUALS) as NodeType[]).forEach(type => {
    const visual = NODE_VISUALS[type];
    (styles as Array<{ selector: string; style: Record<string, unknown> }>).push({
      selector: `node[nodeType = "${type}"]`,
      style: {
        'background-color': visual.color,
        'shape': visual.shape,
        'border-color': visual.color,
      },
    });
  });

  return styles;
}

export interface CytoscapeCanvasOptions {
  /** When true, node positions snap to the grid on drag end and when nodes are added. */
  snapToGrid?: Ref<boolean>;
}

export function useCytoscapeCanvas(containerRef: Ref<HTMLElement | null>, options: CytoscapeCanvasOptions = {}) {
  const store = useCanvasStore();
  const cy = ref<Core | null>(null);
  /** Number of selected (non-group) nodes — > 1 means a multi/box selection is active. */
  const selectedNodeCount = ref(0);
  /** Current viewport, mirrored so the grid background can follow pan/zoom. */
  const viewport = ref({ zoom: 1, pan: { x: 0, y: 0 } });

  const snapEnabled = () => options.snapToGrid?.value === true;

  let selectionSyncQueued = false;
  function queueSelectionSync() {
    if (selectionSyncQueued) return;
    selectionSyncQueued = true;
    // Box selection fires one `select` per element; coalesce into a single update.
    queueMicrotask(() => {
      selectionSyncQueued = false;
      if (!cy.value) return;
      const nodes = cy.value.nodes(':selected').filter(n => !n.data('isGroup'));
      selectedNodeCount.value = nodes.length;
      if (nodes.length > 1) {
        // Inspector edits a single node; hide it while several are selected.
        store.setSelectedNode(null);
      } else if (nodes.length === 1 && store.selectedNodeId !== nodes[0].id()) {
        store.setSelectedNode(nodes[0].id());
      }
    });
  }

  function init() {
    if (!containerRef.value) return;

    cy.value = cytoscape({
      container: containerRef.value,
      elements: [],
      style: buildStylesheet(),
      layout: { name: 'preset' },
      userZoomingEnabled: true,
      userPanningEnabled: true,
      // With panning also enabled, Cytoscape only starts a box selection when a modifier
      // key (shift/ctrl/alt/meta) is held — a plain background drag still pans.
      boxSelectionEnabled: true,
      selectionType: 'single',
    } as CytoscapeOptions);

    // Edge handles via extension
    const cyAny = cy.value as unknown as Record<string, unknown>;
    if (typeof cyAny['edgehandles'] === 'function') {
      (cyAny['edgehandles'] as (opts: unknown) => void)({
        snap: true,
        snapThreshold: 15,
        handleNodes: 'node:not(.annotation)',
        complete: (sourceNode: NodeSingular, targetNode: NodeSingular, addedEdge: cytoscape.EdgeSingular) => {
          const id = `e-${Date.now()}`;
          addedEdge.data('id', id);
          store.addEdge({ id, from: sourceNode.id(), to: targetNode.id() });
        },
      });
    }

    // Selection
    cy.value.on('tap', 'node', evt => {
      if (!evt.target.data('isGroup')) {
        store.setSelectedNode(evt.target.id());
      }
    });
    cy.value.on('tap', evt => {
      if (evt.target === cy.value) store.setSelectedNode(null);
    });
    cy.value.on('select unselect', 'node', queueSelectionSync);

    // Node position changes. When several nodes are dragged together each one fires
    // `dragfree`, so snapping per target covers multi-selection drags too.
    cy.value.on('dragfree', 'node', evt => {
      const node = evt.target as NodeSingular;
      if (snapEnabled() && !node.isParent()) node.position(snapPosition(node.position()));
      const pos = node.position();
      store.updateNode(node.id(), { position: { x: pos.x, y: pos.y } });
      store.setDirty(true);
    });

    cy.value.on('viewport', () => {
      if (!cy.value) return;
      const pan = cy.value.pan();
      viewport.value = { zoom: cy.value.zoom(), pan: { x: pan.x, y: pan.y } };
    });
  }

  function destroy() {
    cy.value?.destroy();
    cy.value = null;
  }

  function addNodeToCanvas(
    type: NodeType,
    position: { x: number; y: number },
    extra: { label?: string; copiedFrom?: string; select?: boolean } = {},
  ): string | undefined {
    if (!cy.value) return;
    let n = cy.value.nodes(`[nodeType = "${type}"]`).length + 1;
    let id = `${type}-${n}`;
    // Counting by type can collide after deletions — find the next free id.
    while (cy.value.getElementById(id).length > 0) { n++; id = `${type}-${n}`; }
    const finalPos = snapEnabled() ? snapPosition(position) : position;
    const label = extra.label ?? id;
    cy.value.add({
      group: 'nodes',
      data: { id, label, nodeType: type, ...(extra.copiedFrom ? { copiedFrom: extra.copiedFrom } : {}) },
      classes: type === 'annotation' ? 'annotation' : undefined,
      position: finalPos,
    });
    const node: import('@/types/canvas.types').CanvasNode = {
      id, type, label, config: {}, position: finalPos,
    };
    store.addNode(node);
    if (extra.select !== false) store.setSelectedNode(id);
    return id;
  }

  function runLayout() {
    if (!cy.value) return;
    const cyAny = cy.value as unknown as { layout: (opts: unknown) => { run(): void } };
    cyAny.layout({
      name: 'dagre',
      rankDir: 'TB',
      rankSep: 80,
      nodeSep: 60,
      animate: true,
      animationDuration: 300,
      animationEasing: 'ease-in-out-cubic',
      fit: true,
      padding: 40,
    }).run();
  }

  function fitView() { cy.value?.fit(undefined, 40); }
  function zoomIn() { if (cy.value) cy.value.zoom(cy.value.zoom() * 1.2); }
  function zoomOut() { if (cy.value) cy.value.zoom(cy.value.zoom() / 1.2); }

  /** Remove every selected element. Returns how many elements were removed. */
  function deleteSelected(): number {
    if (!cy.value) return 0;
    const selected = cy.value.$(':selected');
    selected.forEach(el => {
      if (el.isNode() && !el.data('isGroup')) store.removeNode(el.id());
      if (el.isEdge()) store.removeEdge(el.id());
    });
    const count = selected.length;
    selected.remove();
    return count;
  }

  /**
   * Duplicate the given nodes (offset by one grid cell pair) plus the edges that run
   * between them. The copies become the new selection. Returns the new node ids.
   */
  function duplicateNodes(ids: string[], offset = 40): string[] {
    const c = cy.value;
    if (!c) return [];
    const idMap = new Map<string, string>();
    for (const srcId of ids) {
      const src = c.getElementById(srcId);
      if (src.length === 0 || src.data('isGroup')) continue;
      const pos = src.position();
      const newId = addNodeToCanvas(
        (src.data('nodeType') as NodeType) || 'action',
        { x: pos.x + offset, y: pos.y + offset },
        { label: `${src.data('label') ?? srcId} (copy)`, copiedFrom: srcId, select: false },
      );
      if (newId) idMap.set(srcId, newId);
    }
    let edgeSeq = 0;
    c.edges().forEach(edge => {
      const s = idMap.get(edge.data('source') as string);
      const t = idMap.get(edge.data('target') as string);
      if (!s || !t) return;
      const id = `e-${Date.now()}-${edgeSeq++}`;
      c.add({ group: 'edges', data: { id, source: s, target: t, ...(edge.data('via') ? { via: edge.data('via') } : {}) } });
      store.addEdge({ id, from: s, to: t, via: edge.data('via') as string | undefined });
    });
    const newIds = [...idMap.values()];
    c.elements().unselect();
    newIds.forEach(id => c.getElementById(id).select());
    store.setSelectedNode(newIds.length === 1 ? newIds[0] : null);
    return newIds;
  }

  /** Snap every non-group node to the grid (used when the toggle is switched on). */
  function snapAllNodes() {
    cy.value?.nodes().forEach(n => {
      if (!n.isParent() && !n.data('isGroup')) n.position(snapPosition(n.position()));
    });
  }

  function addGroup(label: string, color = '#6B7280') {
    if (!cy.value) return;
    const id = `group-${Date.now()}`;
    cy.value.add({
      group: 'nodes',
      data: { id, label, isGroup: true },
      classes: 'group',
    });
    store.setGroups([...store.groups, { id, label, nodeIds: [], color }]);
    return id;
  }

  function loadFromNodes(
    nodes: import('@/types/canvas.types').CanvasNode[],
    edges: import('@/types/canvas.types').CanvasEdge[],
  ) {
    if (!cy.value) return;
    cy.value.elements().remove();

    for (const node of nodes) {
      cy.value.add({
        group: 'nodes',
        data: { id: node.id, label: node.label || node.id, nodeType: node.type },
        classes: node.type === 'annotation' ? 'annotation' : undefined,
        position: node.position || { x: Math.random() * 500, y: Math.random() * 400 },
      });
    }

    for (const edge of edges) {
      try {
        cy.value.add({
          group: 'edges',
          data: { id: edge.id, source: edge.from, target: edge.to, label: edge.via },
        });
      } catch { /* skip invalid edges */ }
    }
  }

  onMounted(() => init());
  onUnmounted(() => destroy());

  return {
    cy, selectedNodeCount, viewport,
    addNodeToCanvas, runLayout, fitView, zoomIn, zoomOut, deleteSelected, duplicateNodes, snapAllNodes,
    addGroup, loadFromNodes,
  };
}
