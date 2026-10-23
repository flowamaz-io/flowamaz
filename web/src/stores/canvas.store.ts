import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import type { CanvasNode, CanvasEdge, WorkflowGroup } from '@/types/canvas.types';

const MAX_HISTORY = 50;

export const useCanvasStore = defineStore('canvas', () => {
  const nodes = ref<CanvasNode[]>([]);
  const edges = ref<CanvasEdge[]>([]);
  const groups = ref<WorkflowGroup[]>([]);
  const selectedNodeId = ref<string | null>(null);
  const history = ref<string[]>([]);
  const historyIndex = ref(-1);
  const isDirty = ref(false);
  const isSyncing = ref(false);
  const yamlContent = ref('');

  const selectedNode = computed(() =>
    nodes.value.find(n => n.id === selectedNodeId.value) ?? null
  );

  const canUndo = computed(() => historyIndex.value > 0);
  const canRedo = computed(() => historyIndex.value < history.value.length - 1);
  const undoCount = computed(() => historyIndex.value);

  function pushHistory(yaml: string) {
    // Truncate future history on new action
    history.value = history.value.slice(0, historyIndex.value + 1);
    history.value.push(yaml);
    if (history.value.length > MAX_HISTORY) {
      history.value.shift();
    } else {
      historyIndex.value++;
    }
  }

  function undo(): string | null {
    if (!canUndo.value) return null;
    historyIndex.value--;
    return history.value[historyIndex.value] ?? null;
  }

  function redo(): string | null {
    if (!canRedo.value) return null;
    historyIndex.value++;
    return history.value[historyIndex.value] ?? null;
  }

  function setNodes(newNodes: CanvasNode[]) {
    nodes.value = newNodes;
  }

  function setEdges(newEdges: CanvasEdge[]) {
    edges.value = newEdges;
  }

  function setGroups(newGroups: WorkflowGroup[]) {
    groups.value = newGroups;
  }

  function setSelectedNode(id: string | null) {
    selectedNodeId.value = id;
  }

  function setYaml(yaml: string) {
    yamlContent.value = yaml;
  }

  function setDirty(dirty: boolean) {
    isDirty.value = dirty;
  }

  function setSyncing(syncing: boolean) {
    isSyncing.value = syncing;
  }

  function addNode(node: CanvasNode) {
    nodes.value.push(node);
    isDirty.value = true;
  }

  function updateNode(id: string, updates: Partial<CanvasNode>) {
    const idx = nodes.value.findIndex(n => n.id === id);
    if (idx !== -1) {
      nodes.value[idx] = { ...nodes.value[idx], ...updates };
      isDirty.value = true;
    }
  }

  function removeNode(id: string) {
    nodes.value = nodes.value.filter(n => n.id !== id);
    edges.value = edges.value.filter(e => e.from !== id && e.to !== id);
    if (selectedNodeId.value === id) selectedNodeId.value = null;
    isDirty.value = true;
  }

  function addEdge(edge: CanvasEdge) {
    edges.value.push(edge);
    isDirty.value = true;
  }

  function removeEdge(id: string) {
    edges.value = edges.value.filter(e => e.id !== id);
    isDirty.value = true;
  }

  /**
   * Canvas → YAML editor "reveal node" request. `seq` increments on every request so
   * asking for the same node twice still triggers watchers.
   */
  const yamlRevealRequest = ref<{ nodeId: string; seq: number; explicit: boolean } | null>(null);

  /** `explicit` = user asked directly (context menu) — the editor should take focus and report misses. */
  function requestYamlReveal(nodeId: string, explicit = false) {
    yamlRevealRequest.value = { nodeId, explicit, seq: (yamlRevealRequest.value?.seq ?? 0) + 1 };
  }

  function reset() {
    yamlRevealRequest.value = null;
    nodes.value = [];
    edges.value = [];
    groups.value = [];
    selectedNodeId.value = null;
    history.value = [];
    historyIndex.value = -1;
    isDirty.value = false;
    isSyncing.value = false;
    yamlContent.value = '';
  }

  return {
    nodes, edges, groups, selectedNodeId, selectedNode,
    history, historyIndex, isDirty, isSyncing, yamlContent,
    canUndo, canRedo, undoCount,
    pushHistory, undo, redo,
    setNodes, setEdges, setGroups, setSelectedNode, setYaml, setDirty, setSyncing,
    addNode, updateNode, removeNode, addEdge, removeEdge, reset,
    yamlRevealRequest, requestYamlReveal,
  };
});
