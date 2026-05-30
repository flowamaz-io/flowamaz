<template>
  <div class="flex flex-col h-full bg-[#F8FAFC] overflow-hidden">
    <!-- Toolbar -->
    <CanvasToolbar
      :workflow-name="workflowName"
      :can-undo="canvasStore.canUndo"
      :can-redo="canvasStore.canRedo"
      :undo-count="canvasStore.undoCount"
      :is-dirty="canvasStore.isDirty"
      :show-minimap="showMinimap"
      :saving="props.saving ?? false"
      @undo="handleUndo"
      @redo="handleRedo"
      @tidy="canvas.runLayout"
      @add-group="handleAddGroup"
      @zoom-in="canvas.zoomIn"
      @zoom-out="canvas.zoomOut"
      @fit="canvas.fitView"
      @toggle-minimap="showMinimap = !showMinimap"
      @save="$emit('save')"
      @rename="workflowName = $event"
    />

    <div class="flex flex-1 overflow-hidden">
      <!-- Node Palette -->
      <NodePalette />

      <!-- Canvas area -->
      <div class="flex-1 relative">
        <div
          ref="containerRef"
          class="w-full h-full"
          tabindex="0"
          @dragover.prevent
          @drop="onDrop"
          @contextmenu.prevent="onContextMenu"
          @keydown="onKeydown"
        />

        <!-- Minimap -->
        <div
          v-show="showMinimap"
          class="absolute bottom-4 right-4 w-36 h-28 bg-white border border-gray-200 rounded-lg overflow-hidden shadow-lg pointer-events-none opacity-80"
        >
          <img
            v-if="minimapDataUrl"
            :src="minimapDataUrl"
            class="w-full h-full object-contain"
            alt=""
          >
          <div
            v-else
            class="w-full h-full flex items-center justify-center"
          >
            <span class="text-gray-300 text-xs">Canvas</span>
          </div>
        </div>
      </div>

      <!-- Node Inspector -->
      <NodeInspector
        :node="canvasStore.selectedNode"
        @close="canvasStore.setSelectedNode(null)"
        @update="onNodeUpdate"
        @help="onNodeHelp"
      />

      <!-- YAML panel (shows when no node is selected) -->
      <div
        v-if="!canvasStore.selectedNode && yamlContent"
        class="w-72 bg-white border-l border-gray-200 flex flex-col shrink-0 overflow-hidden"
      >
        <div class="flex items-center justify-between px-3 py-2 border-b border-gray-200 bg-gray-50 shrink-0">
          <span class="text-xs font-medium text-gray-500 uppercase tracking-wider">YAML</span>
          <button
            class="text-xs text-gray-500 hover:text-gray-700 px-2 py-1 rounded hover:bg-gray-200 transition-colors flex items-center gap-1"
            @click="copyYaml"
          >
            <Clipboard class="w-3.5 h-3.5" />
            {{ yamlCopied ? 'Copied!' : 'Copy' }}
          </button>
        </div>
        <div class="flex-1 overflow-auto p-3">
          <pre class="text-xs font-mono text-gray-800 leading-5 whitespace-pre-wrap">{{ yamlContent }}</pre>
        </div>
      </div>
    </div>

    <!-- Context menu -->
    <NodeContextMenu
      :visible="ctxMenu.visible"
      :x="ctxMenu.x"
      :y="ctxMenu.y"
      :node-type="ctxMenu.nodeType"
      @edit="onCtxEdit"
      @duplicate="onCtxDuplicate"
      @add-connected="onCtxAddConnected"
      @help="onCtxHelp"
      @delete="onCtxDelete"
      @configure="onCtxConfigure"
      @add-branch="onCtxAddBranch"
      @test="onCtxTest"
      @preview-gate="onCtxPreviewGate"
      @edit-conditions="onCtxEditConditions"
      @set-default-branch="onCtxSetDefaultBranch"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted, nextTick } from 'vue';
import * as jsYaml from 'js-yaml';
import { useRoute } from 'vue-router';
import { useCytoscapeCanvas } from '@/composables/useCytoscapeCanvas';
import { useCanvasHistory } from '@/composables/useCanvasHistory';
import { useYamlCanvasSync } from '@/composables/useYamlCanvasSync';
import { useCanvasStore } from '@/stores/canvas.store';
import { useWorkspace } from '@/composables/useWorkspace';
import { useHelp } from '@/composables/useHelp';
import { workflowService } from '@/services/workflow.service';
import { useWorkflowStore } from '@/stores/workflow.store';
import NodePalette from './NodePalette.vue';
import CanvasToolbar from './CanvasToolbar.vue';
import NodeInspector from './NodeInspector.vue';
import NodeContextMenu from './NodeContextMenu.vue';
import { Clipboard } from 'lucide-vue-next';
import type { NodeType, CanvasNode } from '@/types/canvas.types';

const props = defineProps<{ initialYaml?: string; saving?: boolean }>();
const emit = defineEmits<{ save: []; yamlChange: [yaml: string] }>();

const route = useRoute();
const ws = useWorkspace();
const containerRef = ref<HTMLElement | null>(null);
const canvasStore = useCanvasStore();
const { snapshot, performUndo, performRedo } = useCanvasHistory();
const { openArticle } = useHelp();

const workflowName = ref('Untitled Workflow');
const showMinimap = ref(true);
const yamlContent = ref('');
const yamlCopied = ref(false);
const minimapDataUrl = ref('');

const canvas = useCytoscapeCanvas(containerRef);
const { syncYamlToCanvas } = useYamlCanvasSync(() => canvas.cy.value);
const workflowStore = useWorkflowStore();

const ctxMenu = ref({ visible: false, x: 0, y: 0, nodeId: '', nodeType: '' });

async function loadWorkflow() {
  const workspaceId = (route.query.workspaceId as string) || ws.currentWorkspaceId.value;
  const workflowId = route.params.id as string;
  if (!workspaceId || !workflowId) return;
  try {
    const wf = await workflowService.get(workspaceId, workflowId);
    if (wf.name) workflowName.value = wf.name;
    if (wf.yamlContent) {
      yamlContent.value = wf.yamlContent;
      await nextTick();
      syncYamlToCanvas(wf.yamlContent);
      // Wait for sync debounce to settle, then tidy the layout, then update minimap
      setTimeout(() => { canvas.runLayout(); setTimeout(updateMinimap, 400); }, 350);
    }
    canvasStore.setDirty(false);
  } catch {
    // canvas stays empty if load fails
  }
}

watch(() => workflowStore.yamlVersion, (v) => {
  if (v > 0) loadWorkflow();
});

function copyYaml() {
  navigator.clipboard.writeText(yamlContent.value);
  yamlCopied.value = true;
  setTimeout(() => { yamlCopied.value = false; }, 2000);
}

// Keyboard shortcuts
function onKeydown(e: KeyboardEvent) {
  if (e.ctrlKey || e.metaKey) {
    if (e.key === 'z') { e.preventDefault(); handleUndo(); }
    if (e.key === 'y' || (e.key === 'z' && e.shiftKey)) { e.preventDefault(); handleRedo(); }
    if (e.key === 's') { e.preventDefault(); emit('save'); }
    if (e.key === 'a') { e.preventDefault(); canvas.cy.value?.elements().select(); }
    if (e.key === 'd') { e.preventDefault(); onCtxDuplicate(); }
  }
  if (e.key === 'Delete' || e.key === 'Backspace') {
    e.preventDefault();
    canvas.deleteSelected();
    snapshotYaml();
  }
  if (e.key === 'Escape') {
    canvasStore.setSelectedNode(null);
    ctxMenu.value.visible = false;
  }
}

function handleUndo() {
  const prev = performUndo();
  if (prev !== null) {
    canvasStore.setSyncing(true);
    syncYamlToCanvas(prev);
    canvasStore.setSyncing(false);
  }
}

function handleRedo() {
  const next = performRedo();
  if (next !== null) {
    canvasStore.setSyncing(true);
    syncYamlToCanvas(next);
    canvasStore.setSyncing(false);
  }
}

// Drag-and-drop from palette
function onDrop(e: DragEvent) {
  const type = e.dataTransfer?.getData('node-type') as NodeType;
  if (!type || !canvas.cy.value) return;
  const rect = containerRef.value!.getBoundingClientRect();
  // Use screen offset as approximate canvas position
  const pos = { x: e.clientX - rect.left, y: e.clientY - rect.top };
  canvas.addNodeToCanvas(type, pos);
  snapshotYaml();
}

function onContextMenu(e: MouseEvent) {
  if (!canvas.cy.value) return;
  // Find the selected node or the one under cursor
  const selected = canvasStore.selectedNodeId;
  if (!selected) return;
  const nodeType = canvas.cy.value?.getElementById(selected).data('nodeType') as string ?? '';
  ctxMenu.value = { visible: true, x: e.clientX, y: e.clientY, nodeId: selected, nodeType };
}

onMounted(async () => {
  document.addEventListener('click', closeCtxMenu);
  // NL generation flow: localStorage YAML takes priority over saved API data
  const storedYaml = localStorage.getItem('fmz_canvas_yaml');
  if (storedYaml) {
    localStorage.removeItem('fmz_canvas_yaml');
    yamlContent.value = storedYaml;
    await nextTick();
    syncYamlToCanvas(storedYaml);
  } else {
    await loadWorkflow();
  }
  // Render initial minimap thumbnail after canvas is ready
  setTimeout(updateMinimap, 500);
});

onUnmounted(() => {
  document.removeEventListener('click', closeCtxMenu);
});

function closeCtxMenu() { ctxMenu.value.visible = false; }

function onNodeUpdate(updates: Partial<CanvasNode>) {
  if (!canvasStore.selectedNodeId) return;
  canvasStore.updateNode(canvasStore.selectedNodeId, updates);
  if (updates.label) {
    canvas.cy.value?.getElementById(canvasStore.selectedNodeId).data('label', updates.label);
  }
  snapshotYaml();
}

function onNodeHelp(nodeType: string) {
  openArticle(`node-types/${nodeType}-nodes`);
}

// Context menu actions
function onCtxEdit() {
  ctxMenu.value.visible = false;
  // Inspector is already open via selectedNode
}

function onCtxDuplicate() {
  const id = ctxMenu.value.nodeId || canvasStore.selectedNodeId;
  if (!id) return;
  const node = canvasStore.nodes.find(n => n.id === id);
  if (!node) return;
  const newId = `${node.type}-${Date.now()}`;
  const newNode: CanvasNode = { ...node, id: newId, position: { x: (node.position?.x ?? 100) + 30, y: (node.position?.y ?? 100) + 30 } };
  canvas.addNodeToCanvas(node.type, newNode.position!);
  canvasStore.updateNode(newId, { label: node.label + ' (copy)' });
  canvas.cy.value?.getElementById(newId).data('label', node.label + ' (copy)');
  snapshotYaml();
  ctxMenu.value.visible = false;
}

function onCtxAddConnected() { ctxMenu.value.visible = false; }

function onCtxHelp() {
  const id = ctxMenu.value.nodeId || canvasStore.selectedNodeId;
  const node = canvasStore.nodes.find(n => n.id === id);
  if (node) openArticle(`node-types/${node.type}-nodes`);
  ctxMenu.value.visible = false;
}

function onCtxDelete() {
  const id = ctxMenu.value.nodeId || canvasStore.selectedNodeId;
  if (!id) return;
  canvas.cy.value?.getElementById(id).remove();
  canvasStore.removeNode(id);
  snapshotYaml();
  ctxMenu.value.visible = false;
}

function onCtxConfigure(_action: string) { ctxMenu.value.visible = false; }
function onCtxAddBranch() { ctxMenu.value.visible = false; }
function onCtxTest(_nodeType: string) { ctxMenu.value.visible = false; }
function onCtxPreviewGate() { ctxMenu.value.visible = false; }
function onCtxEditConditions() { ctxMenu.value.visible = false; }
function onCtxSetDefaultBranch() { ctxMenu.value.visible = false; }

function handleAddGroup() {
  canvas.addGroup('New Group');
  snapshotYaml();
}

function updateMinimap() {
  if (!canvas.cy.value) return;
  try {
    minimapDataUrl.value = canvas.cy.value.png({ output: 'base64uri', full: true, scale: 0.15 }) as string;
  } catch { /* ignore if cy not ready */ }
}

function snapshotYaml() {
  const newYaml = buildYamlFromState();
  yamlContent.value = newYaml;
  snapshot(newYaml);
  canvasStore.setDirty(true);
  emit('yamlChange', newYaml);
  updateMinimap();
}

function buildYamlFromState(): string {
  const cy = canvas.cy.value;

  // Start from the current YAML to preserve all metadata (trigger, variables, node configs).
  // Only replace spec.nodes and spec.edges.
  let parsed: Record<string, unknown> = {
    apiVersion: 'flowamaz/v1',
    kind: 'Workflow',
    metadata: {
      id: workflowName.value.toLowerCase().replace(/\s+/g, '-').replace(/[^a-z0-9-]/g, ''),
      name: workflowName.value,
    },
    spec: { trigger: { type: 'manual' }, nodes: [], edges: [] },
  };

  if (yamlContent.value) {
    try {
      const existing = jsYaml.load(yamlContent.value) as Record<string, unknown>;
      if (existing && typeof existing === 'object') parsed = existing;
    } catch { /* fall back to blank scaffold */ }
  }

  const spec = ((parsed.spec as Record<string, unknown>) ?? {}) as Record<string, unknown>;
  const existingNodes = (spec.nodes as Array<Record<string, unknown>>) ?? [];
  const existingEdges = (spec.edges as Array<Record<string, unknown>>) ?? [];
  const existingNodeMap = new Map(existingNodes.map(n => [n.id as string, n]));
  const existingEdgeMap = new Map(existingEdges.map(e => [e.id as string, e]));

  if (cy) {
    // Use Cytoscape as source of truth — includes pre-loaded nodes and newly-added nodes
    spec.nodes = cy.nodes().filter(n => !n.data('isGroup') && n.id() !== '').map(n => ({
      ...(existingNodeMap.get(n.id()) ?? {}),
      id: n.id(),
      type: n.data('nodeType') || 'action',
      label: n.data('label') || n.id(),
    }));

    spec.edges = cy.edges().map(e => ({
      ...(existingEdgeMap.get(e.id()) ?? {}),
      id: e.id(),
      from: e.data('source'),
      to: e.data('target'),
      ...(e.data('via') ? { via: e.data('via') } : {}),
    }));
  } else {
    // Fallback when cy not available
    const storeNodes = canvasStore.nodes;
    const storeEdges = canvasStore.edges;
    spec.nodes = storeNodes.map(n => ({
      ...(existingNodeMap.get(n.id) ?? {}),
      id: n.id,
      type: n.type,
      label: n.label,
    }));
    spec.edges = storeEdges.map(e => ({
      ...(existingEdgeMap.get(e.id) ?? {}),
      id: e.id,
      from: e.from,
      to: e.to,
      ...(e.via ? { via: e.via } : {}),
    }));
  }

  parsed.spec = spec;
  return jsYaml.dump(parsed, { indent: 2, lineWidth: -1 });
}
</script>
