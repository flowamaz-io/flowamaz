import { watch, ref } from 'vue';
import type { Core } from 'cytoscape';
import { useDebounceFn } from '@vueuse/core';
import { useCanvasStore } from '@/stores/canvas.store';
import type { CanvasNode, CanvasEdge } from '@/types/canvas.types';
import { parseWorkflowGraph } from '@/utils/workflowGraph.util';

const YAML_PARSE_DEBOUNCE = 300;

export function useYamlCanvasSync(getCyInstance: () => Core | null) {
  const store = useCanvasStore();
  const lastSyncedYaml = ref('');

  // Parse workflow YAML into canvas nodes/edges with a real YAML parser. The previous line-by-line
  // regex missed valid YAML styles (e.g. flow mappings `- { id: e1, from: a, to: b }`), which
  // dropped edges from the canvas — and a later canvas save then rewrote spec.edges without them.
  function parseYamlToGraph(yaml: string): { nodes: CanvasNode[]; edges: CanvasEdge[] } | null {
    return parseWorkflowGraph(yaml);
  }

  // YAML carries no positions: remember where removed nodes were so undoing a delete puts
  // them back in place instead of at a random spot.
  const removedPositions = new Map<string, { x: number; y: number }>();

  /** Record where a node sat before it was removed (also called for canvas deletes). */
  function rememberPosition(id: string, pos: { x: number; y: number }) {
    removedPositions.set(id, { x: pos.x, y: pos.y });
  }

  /**
   * `force` re-applies a document even if it was the last one synced — undo/redo return to
   * earlier documents (often the one loaded first) while the canvas has moved on since.
   */
  const syncYamlToCanvas = useDebounceFn((yaml: string, options: { force?: boolean } = {}) => {
    if (!options.force && yaml === lastSyncedYaml.value) return;
    lastSyncedYaml.value = yaml;

    const cy = getCyInstance();
    if (!cy) return;

    const graph = parseYamlToGraph(yaml);
    if (!graph) return;

    store.setSyncing(true);

    // Preserve viewport
    const pan = cy.pan();
    const zoom = cy.zoom();

    // Remove nodes not in new graph
    const newNodeIds = new Set(graph.nodes.map(n => n.id));
    cy.nodes('[id != ""]').forEach(node => {
      if (!node.data('isGroup') && !newNodeIds.has(node.id())) {
        rememberPosition(node.id(), node.position());
        node.remove();
      }
    });

    // Update or add nodes
    for (const node of graph.nodes) {
      const existing = cy.getElementById(node.id);
      if (existing.length > 0) {
        existing.data('label', node.label || node.id);
        existing.data('nodeType', node.type);
      } else {
        cy.add({
          group: 'nodes',
          data: { id: node.id, label: node.label || node.id, nodeType: node.type || 'action' },
          position: node.position || removedPositions.get(node.id) || { x: Math.random() * 400, y: Math.random() * 300 },
        });
      }
    }

    // Remove edges not in new graph
    const newEdgeIds = new Set(graph.edges.map(e => e.id));
    cy.edges().forEach(edge => {
      if (!newEdgeIds.has(edge.id())) edge.remove();
    });

    // Update or add edges
    for (const edge of graph.edges) {
      if (cy.getElementById(edge.id).length === 0) {
        try {
          cy.add({
            group: 'edges',
            data: { id: edge.id, source: edge.from, target: edge.to, via: edge.via },
          });
        } catch { /* source/target may not exist yet */ }
      }
    }

    cy.pan(pan);
    cy.zoom(zoom);
    store.setSyncing(false);
  }, YAML_PARSE_DEBOUNCE);

  // Watch store yaml changes and sync to canvas
  watch(() => store.yamlContent, (yaml) => {
    if (!store.isSyncing) syncYamlToCanvas(yaml);
  });

  return { syncYamlToCanvas, parseYamlToGraph, rememberPosition };
}
