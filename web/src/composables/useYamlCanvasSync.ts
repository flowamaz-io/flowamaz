import { watch, ref } from 'vue';
import type { Core } from 'cytoscape';
import { useDebounceFn } from '@vueuse/core';
import { useCanvasStore } from '@/stores/canvas.store';
import type { CanvasNode, CanvasEdge } from '@/types/canvas.types';

const YAML_PARSE_DEBOUNCE = 300;

export function useYamlCanvasSync(getCyInstance: () => Core | null) {
  const store = useCanvasStore();
  const lastSyncedYaml = ref('');

  // Parse a minimal workflow YAML to extract nodes, edges
  function parseYamlToGraph(yaml: string): { nodes: CanvasNode[]; edges: CanvasEdge[] } | null {
    try {
      // Simple line-by-line YAML extraction for canvas display
      // Full parsing happens on the backend via the validator
      const nodes: CanvasNode[] = [];
      const edges: CanvasEdge[] = [];

      // Use regex to extract node and edge blocks from YAML
      const typeLine = /^\s+type:\s*(.+)$/;
      const labelLine = /^\s+label:\s*["']?(.+?)["']?$/;
      const fromLine = /^\s+from:\s*(.+)$/;
      const toLine = /^\s+to:\s*(.+)$/;
      const viaLine = /^\s+via:\s*(.+)$/;

      const lines = yaml.split('\n');
      let inNodes = false;
      let inEdges = false;
      let current: Partial<CanvasNode & CanvasEdge> | null = null;
      let isEdge = false;

      for (const line of lines) {
        if (line.match(/^\s*nodes:/)) { inNodes = true; inEdges = false; continue; }
        if (line.match(/^\s*edges:/)) { inEdges = true; inNodes = false; continue; }
        if (line.match(/^\s*(groups|variables|trigger|metadata|spec):/) || line.match(/^[a-zA-Z]/)) {
          if (line.match(/^[a-zA-Z]/) && !line.startsWith('  ')) {
            inNodes = false; inEdges = false;
          }
        }

        if ((inNodes || inEdges) && line.match(/^\s+-\s+id:/)) {
          if (current) {
            if (isEdge) edges.push({ id: current.id!, from: current.from!, to: current.to!, via: current.via });
            else nodes.push(current as CanvasNode);
          }
          const idMatch = line.match(/^\s+-\s+id:\s*(.+)$/);
          current = { id: idMatch?.[1]?.trim() ?? '', config: {} };
          isEdge = inEdges;
          continue;
        }

        if (current) {
          const tMatch = line.match(typeLine);
          if (tMatch) { current.type = tMatch[1].trim() as CanvasNode['type']; continue; }
          const lMatch = line.match(labelLine);
          if (lMatch && !isEdge) { current.label = lMatch[1].trim(); continue; }
          const fMatch = line.match(fromLine);
          if (fMatch && isEdge) { (current as CanvasEdge).from = fMatch[1].trim(); continue; }
          const toMatch = line.match(toLine);
          if (toMatch && isEdge) { (current as CanvasEdge).to = toMatch[1].trim(); continue; }
          const vMatch = line.match(viaLine);
          if (vMatch && isEdge) { (current as CanvasEdge).via = vMatch[1].trim(); continue; }
        }
      }

      if (current) {
        if (isEdge) edges.push({ id: current.id!, from: (current as CanvasEdge).from!, to: (current as CanvasEdge).to!, via: (current as CanvasEdge).via });
        else if ((current as CanvasNode).type) nodes.push(current as CanvasNode);
      }

      return nodes.length > 0 ? { nodes, edges } : null;
    } catch {
      return null;
    }
  }

  const syncYamlToCanvas = useDebounceFn((yaml: string) => {
    if (yaml === lastSyncedYaml.value) return;
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
          position: node.position || { x: Math.random() * 400, y: Math.random() * 300 },
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

  return { syncYamlToCanvas, parseYamlToGraph };
}
