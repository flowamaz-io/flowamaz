<template>
  <div class="flex flex-col h-full bg-gray-950 overflow-hidden">
    <!-- Toolbar -->
    <CanvasToolbar
      :workflow-name="workflowName"
      :can-undo="canvasStore.canUndo"
      :can-redo="canvasStore.canRedo"
      :undo-count="canvasStore.undoCount"
      :is-dirty="canvasStore.isDirty"
      :show-minimap="showMinimap"
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
          @dragover.prevent
          @drop="onDrop"
          @contextmenu.prevent="onContextMenu"
          @keydown="onKeydown"
          tabindex="0"
        />

        <!-- Minimap -->
        <div
          v-show="showMinimap"
          class="absolute bottom-4 right-4 w-36 h-28 bg-gray-800 border border-gray-600 rounded-lg overflow-hidden shadow-lg pointer-events-none opacity-80"
        >
          <div ref="minimapRef" class="w-full h-full" />
        </div>
      </div>

      <!-- Node Inspector -->
      <NodeInspector
        :node="canvasStore.selectedNode"
        @close="canvasStore.setSelectedNode(null)"
        @update="onNodeUpdate"
        @help="onNodeHelp"
      />
    </div>

    <!-- Context menu -->
    <NodeContextMenu
      :visible="ctxMenu.visible"
      :x="ctxMenu.x"
      :y="ctxMenu.y"
      @edit="onCtxEdit"
      @duplicate="onCtxDuplicate"
      @add-connected="onCtxAddConnected"
      @help="onCtxHelp"
      @delete="onCtxDelete"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue';
import { useCytoscapeCanvas } from '@/composables/useCytoscapeCanvas';
import { useCanvasHistory } from '@/composables/useCanvasHistory';
import { useYamlCanvasSync } from '@/composables/useYamlCanvasSync';
import { useCanvasStore } from '@/stores/canvas.store';
import { useHelp } from '@/composables/useHelp';
import NodePalette from './NodePalette.vue';
import CanvasToolbar from './CanvasToolbar.vue';
import NodeInspector from './NodeInspector.vue';
import NodeContextMenu from './NodeContextMenu.vue';
import type { NodeType, CanvasNode } from '@/types/canvas.types';

defineProps<{ initialYaml?: string }>();
const emit = defineEmits<{ save: []; yamlChange: [yaml: string] }>();

const containerRef = ref<HTMLElement | null>(null);
const minimapRef = ref<HTMLElement | null>(null);
const canvasStore = useCanvasStore();
const { snapshot, performUndo, performRedo } = useCanvasHistory();
const { openArticle } = useHelp();

const workflowName = ref('Untitled Workflow');
const showMinimap = ref(true);

const canvas = useCytoscapeCanvas(containerRef);
const { syncYamlToCanvas } = useYamlCanvasSync(() => canvas.cy.value);

const ctxMenu = ref({ visible: false, x: 0, y: 0, nodeId: '' });

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
  ctxMenu.value = { visible: true, x: e.clientX, y: e.clientY, nodeId: selected };
}

onMounted(() => {
  document.addEventListener('click', closeCtxMenu);
  // Initialize minimap after canvas is ready
  setTimeout(() => {
    if (minimapRef.value && canvas.cy.value) {
      try {
        const nav = (canvas.cy.value as unknown as { navigator: (opts: unknown) => void }).navigator;
        if (nav) {
          nav({
            container: minimapRef.value,
            viewLiveFramerate: 0,
            thumbnailEventFramerate: 30,
          });
        }
      } catch { /* navigator may not be available */ }
    }
  }, 200);
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

function handleAddGroup() {
  canvas.addGroup('New Group');
  snapshotYaml();
}

function snapshotYaml() {
  // Build minimal YAML from current state and snapshot it
  const yaml = buildYamlFromState();
  snapshot(yaml);
  canvasStore.setYaml(yaml);
  canvasStore.setDirty(true);
  emit('yamlChange', yaml);
}

function buildYamlFromState(): string {
  const nodes = canvasStore.nodes;
  const edges = canvasStore.edges;
  const lines = [
    'apiVersion: flowamaz/v1',
    'kind: Workflow',
    'metadata:',
    `  id: ${workflowName.value.toLowerCase().replace(/\s+/g, '-').replace(/[^a-z0-9-]/g, '')}`,
    `  name: "${workflowName.value}"`,
    'spec:',
    '  trigger:',
    '    type: manual',
    '  nodes:',
    ...nodes.map(n => [
      `    - id: ${n.id}`,
      `      type: ${n.type}`,
      `      label: "${n.label}"`,
    ].join('\n')),
    '  edges:',
    ...edges.map(e => [
      `    - id: ${e.id}`,
      `      from: ${e.from}`,
      `      to: ${e.to}`,
      ...(e.via ? [`      via: ${e.via}`] : []),
    ].join('\n')),
  ];
  return lines.join('\n');
}
</script>
