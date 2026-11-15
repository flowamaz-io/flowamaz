import { ref, onMounted, onUnmounted } from 'vue';
import type { Ref } from 'vue';
import cytoscape from 'cytoscape';
import type { Core, NodeSingular, CytoscapeOptions } from 'cytoscape';
import dagre from 'cytoscape-dagre';
import { NODE_VISUALS } from './types';
import type { NodeType } from './types';
import type { CanvasModel } from './model';
import { snapPosition } from './canvasMath';

// Register extensions once
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
      // Another person in a shared editing session has this node selected (their colour, dashed).
      selector: 'node.collab-selected',
      style: {
        'border-color': 'data(collabColor)',
        'border-width': 3,
        'border-style': 'dashed',
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
      // Connect mode: the node a connection is being drawn from, and the one it would go to.
      selector: 'node.connect-source, node.connect-target',
      style: {
        'border-color': '#6366F1',
        'border-width': 3,
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

/**
 * Id of the node drawn at a point in rendered (container-relative) coordinates, or null.
 * Group boxes are ignored (they have no node actions); the topmost (last drawn) node wins.
 */
export function nodeIdAtRenderedPoint(cy: Core, x: number, y: number): string | null {
  let hit: string | null = null;
  cy.nodes().forEach((node) => {
    if (node.id() === '' || node.data('isGroup') || node.isParent()) return;
    const bb = node.renderedBoundingBox({ includeLabels: false, includeOverlays: false });
    if (x >= bb.x1 && x <= bb.x2 && y >= bb.y1 && y <= bb.y2) hit = node.id();
  });
  return hit;
}

export interface CytoscapeCanvasOptions {
  /** When true, node positions snap to the grid on drag end and when nodes are added. */
  snapToGrid?: Ref<boolean>;
}

export function useCytoscapeCanvas(store: CanvasModel, containerRef: Ref<HTMLElement | null>, options: CytoscapeCanvasOptions = {}) {
  const cy = ref<Core | null>(null);
  /** Number of selected (non-group) nodes — > 1 means a multi/box selection is active. */
  const selectedNodeCount = ref(0);
  /** Current viewport, mirrored so the grid background can follow pan/zoom. */
  const viewport = ref({ zoom: 1, pan: { x: 0, y: 0 } });
  /** Connect mode: dragging from one node onto another draws an edge (nodes don't move meanwhile). */
  const connectMode = ref(false);
  /** The line being dragged in Connect mode, in rendered (screen) coordinates of the canvas. */
  const connectLine = ref<{ x1: number; y1: number; x2: number; y2: number } | null>(null);
  let connectSource: NodeSingular | null = null;
  let editableNow = true;
  const onConnectHandlers: Array<(from: string, to: string) => void> = [];

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

    // Connect mode (see setConnectMode): the line being dragged is drawn in an overlay, never as a graph
    // element. Adding and removing temporary edges between two nodes mid-drag (as cytoscape-edgehandles did)
    // left a removed edge in Cytoscape's cached list of parallel edges, and the canvas froze.
    cy.value.on('tapstart', 'node', (evt) => {
      if (!connectMode.value) return;
      const node = evt.target as NodeSingular;
      if (node.data('isGroup') || node.hasClass('annotation')) return;
      connectSource = node;
      node.addClass('connect-source');
      const p = node.renderedPosition();
      connectLine.value = { x1: p.x, y1: p.y, x2: p.x, y2: p.y };
      cy.value?.userPanningEnabled(false);
    });
    cy.value.on('tapdrag', (evt) => {
      if (!connectSource || !connectLine.value) return;
      const p = evt.renderedPosition;
      connectLine.value = { ...connectLine.value, x2: p.x, y2: p.y };
      const target = connectTargetAt(p);
      cy.value?.nodes('.connect-target').forEach((n) => { if (!target || !n.same(target)) n.removeClass('connect-target'); });
      target?.addClass('connect-target');
    });
    cy.value.on('tapend', (evt) => {
      if (!connectSource) return;
      const source = connectSource;
      const target = connectTargetAt(evt.renderedPosition);
      endConnectGesture();
      if (target) onConnectHandlers.forEach((h) => h(source.id(), target.id()));
    });

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
    });

    cy.value.on('viewport', () => {
      if (!cy.value) return;
      const pan = cy.value.pan();
      viewport.value = { zoom: cy.value.zoom(), pan: { x: pan.x, y: pan.y } };
    });
  }

  /** Watching only: nodes can't be dragged and edges can't be drawn. */
  function setEditable(editable: boolean) {
    editableNow = editable;
    if (!editable) setConnectMode(false);
    cy.value?.autoungrabify(!editable);
  }

  /** In Connect mode nodes don't move: a drag from a node draws a connection instead. */
  function setConnectMode(on: boolean) {
    const next = on && editableNow;
    if (!next) endConnectGesture();
    connectMode.value = next;
    cy.value?.autoungrabify(next || !editableNow);
  }

  /** Called with (from, to) when a connection is drawn; the canvas checks it and writes it to the YAML. */
  function onConnect(handler: (from: string, to: string) => void) {
    onConnectHandlers.push(handler);
  }

  /** The node under a rendered point that a connection from the current source may go to. */
  function connectTargetAt(p: { x: number; y: number }): NodeSingular | null {
    if (!cy.value || !connectSource) return null;
    const hits = cy.value.nodes().filter((n) => {
      if (n.same(connectSource!) || n.data('isGroup') || n.hasClass('annotation')) return false;
      const b = n.renderedBoundingBox({ includeLabels: false });
      return p.x >= b.x1 && p.x <= b.x2 && p.y >= b.y1 && p.y <= b.y2;
    });
    return hits.length > 0 ? hits[hits.length - 1] : null;
  }

  function endConnectGesture() {
    connectSource = null;
    connectLine.value = null;
    cy.value?.nodes('.connect-source, .connect-target').removeClass('connect-source connect-target');
    cy.value?.userPanningEnabled(true);
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
    // The lowest free number: counting nodes of the type skipped numbers when they had other ids
    // (an Action called "review" made the first new one action-2).
    let n = 1;
    let id = `${type}-${n}`;
    while (cy.value.getElementById(id).length > 0) { n++; id = `${type}-${n}`; }
    const finalPos = snapEnabled() ? snapPosition(position) : position;
    const label = extra.label ?? id;
    cy.value.add({
      group: 'nodes',
      data: { id, label, nodeType: type, ...(extra.copiedFrom ? { copiedFrom: extra.copiedFrom } : {}) },
      classes: type === 'annotation' ? 'annotation' : undefined,
      position: finalPos,
    });
    const node: import('./types').CanvasNode = {
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
    nodes: import('./types').CanvasNode[],
    edges: import('./types').CanvasEdge[],
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
    cy, selectedNodeCount, viewport, setEditable, connectMode, setConnectMode, connectLine, onConnect,
    addNodeToCanvas, runLayout, fitView, zoomIn, zoomOut, deleteSelected, duplicateNodes, snapAllNodes,
    addGroup, loadFromNodes,
  };
}
