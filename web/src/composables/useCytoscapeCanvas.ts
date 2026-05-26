import { ref, onMounted, onUnmounted } from 'vue';
import type { Ref } from 'vue';
import cytoscape from 'cytoscape';
import type { Core, NodeSingular, CytoscapeOptions } from 'cytoscape';
import edgehandles from 'cytoscape-edgehandles';
import dagre from 'cytoscape-dagre';
import { NODE_VISUALS } from '@/types/canvas.types';
import type { NodeType } from '@/types/canvas.types';
import { useCanvasStore } from '@/stores/canvas.store';

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
        'border-color': '#818CF8',
        'border-width': 3,
      },
    },
    {
      selector: 'edge',
      style: {
        'width': 2,
        'line-color': '#6B7280',
        'target-arrow-color': '#6B7280',
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
        'color': '#9CA3AF',
        'text-rotation': 'autorotate',
        'text-background-color': '#1F2937',
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
        'color': '#9CA3AF',
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

export function useCytoscapeCanvas(containerRef: Ref<HTMLElement | null>) {
  const store = useCanvasStore();
  const cy = ref<Core | null>(null);

  function init() {
    if (!containerRef.value) return;

    cy.value = cytoscape({
      container: containerRef.value,
      elements: [],
      style: buildStylesheet(),
      layout: { name: 'preset' },
      userZoomingEnabled: true,
      userPanningEnabled: true,
      boxSelectionEnabled: false,
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

    // Node position changes
    cy.value.on('dragfree', 'node', evt => {
      const pos = evt.target.position();
      store.updateNode(evt.target.id(), { position: { x: pos.x, y: pos.y } });
      store.setDirty(true);
    });
  }

  function destroy() {
    cy.value?.destroy();
    cy.value = null;
  }

  function addNodeToCanvas(type: NodeType, position: { x: number; y: number }) {
    if (!cy.value) return;
    const typeCount = cy.value.nodes(`[nodeType = "${type}"]`).length;
    const id = `${type}-${typeCount + 1}`;
    cy.value.add({
      group: 'nodes',
      data: { id, label: id, nodeType: type },
      classes: type === 'annotation' ? 'annotation' : undefined,
      position,
    });
    const node: import('@/types/canvas.types').CanvasNode = {
      id, type, label: id, config: {}, position,
    };
    store.addNode(node);
    store.setSelectedNode(id);
    return id;
  }

  function runLayout() {
    if (!cy.value) return;
    const cyAny = cy.value as unknown as { layout: (opts: unknown) => { run(): void } };
    cyAny.layout({
      name: 'dagre',
      rankDir: 'TB',
      ranksep: 80,
      nodesep: 60,
      animate: true,
      animationDuration: 300,
    }).run();
  }

  function fitView() { cy.value?.fit(undefined, 40); }
  function zoomIn() { if (cy.value) cy.value.zoom(cy.value.zoom() * 1.2); }
  function zoomOut() { if (cy.value) cy.value.zoom(cy.value.zoom() / 1.2); }

  function deleteSelected() {
    if (!cy.value) return;
    const selected = cy.value.$(':selected');
    selected.forEach(el => {
      if (el.isNode() && !el.data('isGroup')) store.removeNode(el.id());
      if (el.isEdge()) store.removeEdge(el.id());
    });
    selected.remove();
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

  return { cy, addNodeToCanvas, runLayout, fitView, zoomIn, zoomOut, deleteSelected, addGroup, loadFromNodes };
}
