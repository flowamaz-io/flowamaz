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
      :snap-to-grid="snapToGridEnabled"
      :saving="props.saving ?? false"
      @undo="handleUndo"
      @redo="handleRedo"
      @tidy="canvas.runLayout"
      @add-group="handleAddGroup"
      @zoom-in="canvas.zoomIn"
      @zoom-out="canvas.zoomOut"
      @fit="canvas.fitView"
      @toggle-minimap="showMinimap = !showMinimap"
      @toggle-snap="toggleSnap"
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
          class="w-full h-full outline-none"
          :style="gridBackgroundStyle"
          tabindex="0"
          @dragover.prevent
          @drop="onDrop"
          @contextmenu.prevent="onContextMenu"
          @keydown="onKeydown"
        />

        <!-- Box-selection hint / multi-selection count -->
        <div
          class="absolute top-3 left-3 text-[11px] text-gray-500 bg-white/90 border border-gray-200 rounded-md px-2 py-1 shadow-sm pointer-events-none"
        >
          <template v-if="canvas.selectedNodeCount.value > 1">
            {{ canvas.selectedNodeCount.value }} nodes selected — Delete removes, Ctrl+D duplicates
          </template>
          <template v-else>
            Shift + drag to box-select
          </template>
        </div>

        <!-- Minimap -->
        <CanvasMinimap
          v-show="showMinimap"
          :cy="canvas.cy.value"
          :visible="showMinimap"
        />
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
      @show-in-yaml="onCtxShowInYaml"
    />

    <!-- Node config panel (prompt 06-02) -->
    <NodeConfigPanel
      :open="configPanel.open"
      :node="configPanel.node"
      :workspace-id="ws.currentWorkspaceId.value ?? ''"
      :variables="configVariables"
      :workflow-trigger-type="workflowTriggerType"
      @close="configPanel = { open: false, node: null }"
      @save="applyNodeConfig"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue';
import * as jsYaml from 'js-yaml';
import { useRoute } from 'vue-router';
import { useDebounceFn } from '@vueuse/core';
import { useCytoscapeCanvas, nodeIdAtRenderedPoint as nodeIdAt } from '@/composables/useCytoscapeCanvas';
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
import NodeConfigPanel, { type ConfigNode } from './NodeConfigPanel.vue';
import CanvasMinimap from './CanvasMinimap.vue';
import { GRID_SIZE, gridDotSpacing, loadSnapPreference, renderedToModel, saveSnapPreference } from '@/utils/canvas.util';
import type { WorkflowVariable } from './shared/VariableAutocomplete.vue';
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
const snapToGridEnabled = ref(loadSnapPreference());

const canvas = useCytoscapeCanvas(containerRef, { snapToGrid: snapToGridEnabled });

// Dot grid that follows pan/zoom. Spacing is a multiple of the 20px snap grid, so with
// snapping on, node centres always land on a dot.
const gridBackgroundStyle = computed(() => {
  const { zoom, pan } = canvas.viewport.value;
  const step = gridDotSpacing(zoom, GRID_SIZE);
  const color = snapToGridEnabled.value ? '#94A3B8' : '#CBD5E1';
  return {
    backgroundImage: `radial-gradient(circle, ${color} 1px, transparent 1.5px)`,
    backgroundSize: `${step}px ${step}px`,
    // Dots are drawn at the centre of each tile; shift by half a tile so they sit on model multiples.
    backgroundPosition: `${pan.x - step / 2}px ${pan.y - step / 2}px`,
  };
});

function toggleSnap() {
  snapToGridEnabled.value = !snapToGridEnabled.value;
  saveSnapPreference(snapToGridEnabled.value);
  // Positions are not stored in the YAML, so snapping existing nodes needs no history entry.
  if (snapToGridEnabled.value) canvas.snapAllNodes();
}
const { syncYamlToCanvas, rememberPosition } = useYamlCanvasSync(() => canvas.cy.value);
const workflowStore = useWorkflowStore();

const ctxMenu = ref({ visible: false, x: 0, y: 0, nodeId: '', nodeType: '' });

// ── Node config panel (prompt 06-02) ─────────────────────────────────────────
const configPanel = ref<{ open: boolean; node: ConfigNode | null }>({ open: false, node: null });
const configVariables = ref<WorkflowVariable[]>([]);

function buildConfigNode(nodeId: string): ConfigNode | null {
  if (!nodeId) return null;
  let nodeFromYaml: Record<string, unknown> | undefined;
  try {
    const parsed = jsYaml.load(yamlContent.value) as Record<string, unknown> | undefined;
    const spec = (parsed?.spec as Record<string, unknown>) ?? {};
    const nodes = (spec.nodes as Array<Record<string, unknown>>) ?? [];
    nodeFromYaml = nodes.find((n) => n.id === nodeId);
  } catch { /* fall back to canvas data */ }
  const cyNode = canvas.cy.value?.getElementById(nodeId);
  const type = (nodeFromYaml?.type as string) ?? (cyNode?.data('nodeType') as string) ?? 'action';
  const label = (nodeFromYaml?.label as string) ?? (cyNode?.data('label') as string) ?? nodeId;
  const config = (nodeFromYaml?.config as Record<string, unknown>) ?? {};
  return { id: nodeId, type, label, config };
}

function refreshConfigVariables(): void {
  const vars: WorkflowVariable[] = [];
  try {
    const parsed = jsYaml.load(yamlContent.value) as Record<string, unknown> | undefined;
    const spec = (parsed?.spec as Record<string, unknown>) ?? {};
    // spec.variables is a map in flowamaz/v1 (`name: { type, required }`); older drafts used a list.
    const declared = spec.variables;
    if (Array.isArray(declared)) {
      for (const v of declared as Array<{ name?: string; type?: string }>) {
        if (v?.name) vars.push({ name: v.name, type: v.type ?? 'string' });
      }
    } else if (declared && typeof declared === 'object') {
      for (const [name, def] of Object.entries(declared as Record<string, { type?: string } | null>)) {
        vars.push({ name, type: def?.type ?? 'string' });
      }
    }
    // Node outputs are addressable as variables too.
    const nodes = (spec.nodes as Array<Record<string, unknown>>) ?? [];
    for (const n of nodes) {
      const out = (n.config as Record<string, unknown>)?.outputVariable as string | undefined;
      if (out) vars.push({ name: out, type: 'string' });
    }
  } catch { /* no variables available */ }
  configVariables.value = vars;
}

function openConfigPanel(nodeId: string): void {
  const node = buildConfigNode(nodeId);
  if (!node) return;
  refreshConfigVariables();
  configPanel.value = { open: true, node };
  ctxMenu.value.visible = false;
}

function applyNodeConfig(payload: { id: string; label: string; config: Record<string, unknown> }): void {
  const opened = configPanel.value.node;
  // "Save node" with nothing changed must leave the YAML byte-for-byte as it is (no re-dump,
  // no dirty flag, no undo step).
  if (opened && opened.id === payload.id && opened.label === payload.label
    && JSON.stringify(opened.config) === JSON.stringify(payload.config)) {
    configPanel.value = { open: false, node: null };
    return;
  }
  // Merge the config + label into the YAML, then rebuild so the canvas re-renders.
  try {
    const parsed = (jsYaml.load(yamlContent.value) as Record<string, unknown>) ?? {};
    const spec = (parsed.spec as Record<string, unknown>) ?? (parsed.spec = {});
    const nodes = (spec.nodes as Array<Record<string, unknown>>) ?? (spec.nodes = []);
    const existing = nodes.find((n) => n.id === payload.id);
    if (existing) {
      existing.label = payload.label;
      existing.config = payload.config;
    } else {
      nodes.push({ id: payload.id, type: configPanel.value.node?.type ?? 'action', label: payload.label, config: payload.config });
    }
    yamlContent.value = jsYaml.dump(parsed, { indent: 2, lineWidth: -1 });
  } catch { /* leave YAML unchanged on parse failure */ }

  canvas.cy.value?.getElementById(payload.id).data('label', payload.label);
  snapshotYaml();
  syncYamlToCanvas(yamlContent.value);
  configPanel.value = { open: false, node: null };
}

/** `startHistory` = first load of this canvas: the loaded document becomes the undo baseline. */
async function loadWorkflow(options: { startHistory?: boolean } = {}) {
  const workspaceId = (route.query.workspaceId as string) || ws.currentWorkspaceId.value;
  const workflowId = route.params.id as string;
  if (!workspaceId || !workflowId) {
    if (options.startHistory) canvasStore.resetHistory(buildYamlFromState());
    return;
  }
  try {
    const wf = await workflowService.get(workspaceId, workflowId);
    if (wf.name) workflowName.value = wf.name;
    if (wf.yamlContent) {
      yamlContent.value = wf.yamlContent;
      await nextTick();
      syncYamlToCanvas(wf.yamlContent);
      // Wait for sync debounce to settle, then tidy the layout, then update minimap
      // (the minimap re-renders itself on layoutstop)
      setTimeout(() => { canvas.runLayout(); }, 350);
    }
    if (options.startHistory) canvasStore.resetHistory(wf.yamlContent || buildYamlFromState());
    canvasStore.setDirty(false);
  } catch {
    // canvas stays empty if load fails
    if (options.startHistory) canvasStore.resetHistory(buildYamlFromState());
  }
}

watch(() => workflowStore.yamlVersion, (v) => {
  if (v > 0) loadWorkflow();
});

// Edits typed in the YAML editor reach the canvas through the parent's v-model. Adopt them as
// the canvas document (when they parse), so the next canvas edit or "Save node" builds on
// them instead of silently reverting them to the canvas's older copy.
// Debounced so a burst of typing becomes one undo step, not one per keystroke.
const adoptEditorYaml = useDebounceFn((next: string) => {
  if (next === yamlContent.value) return;
  let doc: unknown;
  try { doc = jsYaml.load(next); } catch { return; }
  if (!doc || typeof doc !== 'object') return;
  yamlContent.value = next;
  syncYamlToCanvas(next);
  snapshot(next);
}, 500);
watch(() => props.initialYaml, (next) => {
  if (next !== undefined && next !== yamlContent.value) void adoptEditorYaml(next);
});

const workflowTriggerType = computed<string>(() => {
  try {
    const parsed = jsYaml.load(yamlContent.value) as Record<string, unknown> | undefined;
    const trigger = (parsed?.spec as Record<string, unknown> | undefined)?.trigger as Record<string, unknown> | undefined;
    return typeof trigger?.type === 'string' ? trigger.type : '';
  } catch {
    return '';
  }
});

function copyYaml() {
  navigator.clipboard.writeText(yamlContent.value);
  yamlCopied.value = true;
  setTimeout(() => { yamlCopied.value = false; }, 2000);
}

// Keyboard shortcuts
function onKeydown(e: KeyboardEvent) {
  if (e.ctrlKey || e.metaKey) {
    // Shift / Caps Lock report upper-case keys ("Z"), so compare case-insensitively.
    const key = e.key.toLowerCase();
    if (key === 'z' && !e.shiftKey) { e.preventDefault(); handleUndo(); }
    if (key === 'y' || (key === 'z' && e.shiftKey)) { e.preventDefault(); handleRedo(); }
    // The editor view also listens for Ctrl/Cmd+S on window; handle it once here.
    if (key === 's') { e.preventDefault(); e.stopPropagation(); emit('save'); }
    if (key === 'a') { e.preventDefault(); canvas.cy.value?.elements().select(); }
    if (key === 'd') { e.preventDefault(); onCtxDuplicate(); }
  }
  if (e.key === 'Delete' || e.key === 'Backspace') {
    e.preventDefault();
    // A whole box selection is removed in one go and recorded as a single undo step.
    if (canvas.deleteSelected() > 0) snapshotYaml();
  }
  if (e.key === 'Escape') {
    canvasStore.setSelectedNode(null);
    ctxMenu.value.visible = false;
  }
}

/**
 * Restore a history entry: it becomes the canvas document, the YAML editor follows, and the
 * canvas is re-synced even when the entry equals the document first loaded.
 */
function applyHistoryEntry(entry: string) {
  yamlContent.value = entry;
  syncYamlToCanvas(entry, { force: true });
  canvasStore.setSelectedNode(null);
  canvasStore.setDirty(true);
  emit('yamlChange', entry);
}

function handleUndo() {
  const prev = performUndo();
  if (prev !== null) applyHistoryEntry(prev);
}

function handleRedo() {
  const next = performRedo();
  if (next !== null) applyHistoryEntry(next);
}

// Drag-and-drop from palette
function onDrop(e: DragEvent) {
  const type = e.dataTransfer?.getData('node-type') as NodeType;
  if (!type || !canvas.cy.value) return;
  const rect = containerRef.value!.getBoundingClientRect();
  // Convert the drop point to model coordinates so it is correct at any pan/zoom
  // (addNodeToCanvas applies grid snap when enabled).
  const pos = renderedToModel(
    { x: e.clientX - rect.left, y: e.clientY - rect.top },
    canvas.cy.value.pan(),
    canvas.cy.value.zoom(),
  );
  canvas.addNodeToCanvas(type, pos);
  snapshotYaml();
}

async function loadWorkflowName(): Promise<void> {
  const workspaceId = (route.query.workspaceId as string) || ws.currentWorkspaceId.value;
  const workflowId = route.params.id as string;
  if (!workspaceId || !workflowId) return;
  try {
    const wf = await workflowService.get(workspaceId, workflowId);
    if (wf.name) workflowName.value = wf.name;
  } catch {
    // Title stays as-is if the workflow can't be loaded.
  }
}

function onContextMenu(e: MouseEvent) {
  const cy = canvas.cy.value;
  if (!cy || !containerRef.value) return;
  const rect = containerRef.value.getBoundingClientRect();
  const targetId = nodeIdAt(cy, e.clientX - rect.left, e.clientY - rect.top);
  if (!targetId) {
    // Right-click on empty canvas: nothing to act on.
    ctxMenu.value.visible = false;
    return;
  }
  // Right-clicking a node selects it, like a left click — unless it's part of a multi-selection,
  // which is kept so Delete / Duplicate apply to the whole selection.
  const selection = selectedNodeIds();
  if (!(selection.length > 1 && selection.includes(targetId))) {
    cy.elements().unselect();
    cy.getElementById(targetId).select();
    canvasStore.setSelectedNode(targetId);
  }
  const nodeType = (cy.getElementById(targetId).data('nodeType') as string | undefined) ?? '';
  ctxMenu.value = { visible: true, x: e.clientX, y: e.clientY, nodeId: targetId, nodeType };
}

onMounted(async () => {
  document.addEventListener('click', closeCtxMenu);
  // NL generation flow: localStorage YAML takes priority over saved API data
  const storedYaml = localStorage.getItem('fmz_canvas_yaml');
  if (storedYaml) {
    localStorage.removeItem('fmz_canvas_yaml');
    yamlContent.value = storedYaml;
    canvasStore.resetHistory(storedYaml);
    await nextTick();
    syncYamlToCanvas(storedYaml);
    // The NL-generation path skips loadWorkflow(); still resolve the real title from the API
    // so the toolbar shows the workflow name, not "Untitled Workflow".
    void loadWorkflowName();
  } else {
    await loadWorkflow({ startHistory: true });
  }
  // Clicking or box-selecting on the canvas gives it keyboard focus, so Ctrl/Cmd+Z, Delete and
  // Ctrl/Cmd+D reach onKeydown even when Cytoscape swallows the mousedown.
  canvas.cy.value?.on('tapstart', () => { containerRef.value?.focus({ preventScroll: true }); });
  // Remember where removed nodes sat, so undoing a delete restores them in place.
  canvas.cy.value?.on('remove', 'node', (evt) => { rememberPosition(evt.target.id(), evt.target.position()); });
  // Double-click a node to open its configuration panel (prompt 06-02).
  canvas.cy.value?.on('dbltap', 'node', (evt) => {
    const id = evt.target.id();
    if (id) openConfigPanel(id);
  });
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
  openConfigPanel(ctxMenu.value.nodeId || canvasStore.selectedNodeId || '');
}

function selectedNodeIds(): string[] {
  const cy = canvas.cy.value;
  if (!cy) return [];
  return cy.nodes(':selected').filter(n => !n.data('isGroup')).map(n => n.id());
}

/** Ids the context-menu / shortcut action applies to: the whole selection if the target is in it. */
function actionTargetIds(): string[] {
  const target = ctxMenu.value.visible ? ctxMenu.value.nodeId : '';
  const selection = selectedNodeIds();
  if (target && !selection.includes(target)) return [target];
  if (selection.length > 0) return selection;
  return canvasStore.selectedNodeId ? [canvasStore.selectedNodeId] : [];
}

function onCtxDuplicate() {
  const ids = actionTargetIds();
  ctxMenu.value.visible = false;
  if (ids.length === 0) return;
  const created = canvas.duplicateNodes(ids);
  // One snapshot for the whole batch → one undo step.
  if (created.length > 0) snapshotYaml();
}

function onCtxShowInYaml() {
  const id = ctxMenu.value.nodeId || canvasStore.selectedNodeId;
  ctxMenu.value.visible = false;
  if (id) canvasStore.requestYamlReveal(id, true);
}

// Selecting a single node scrolls the YAML editor to its definition (passive: no focus steal).
watch(() => canvasStore.selectedNodeId, (id) => {
  if (id) canvasStore.requestYamlReveal(id, false);
});

function onCtxAddConnected() { ctxMenu.value.visible = false; }

function onCtxHelp() {
  const id = ctxMenu.value.nodeId || canvasStore.selectedNodeId;
  const node = canvasStore.nodes.find(n => n.id === id);
  if (node) openArticle(`node-types/${node.type}-nodes`);
  ctxMenu.value.visible = false;
}

function onCtxDelete() {
  const ids = actionTargetIds();
  ctxMenu.value.visible = false;
  const cy = canvas.cy.value;
  if (!cy || ids.length === 0) return;
  // Route through deleteSelected so connected edges are dropped from the store too,
  // and the whole batch lands as a single undo step.
  cy.elements().unselect();
  ids.forEach(id => cy.getElementById(id).select());
  if (canvas.deleteSelected() > 0) snapshotYaml();
}

function onCtxConfigure(_action: string) { openConfigPanel(ctxMenu.value.nodeId || canvasStore.selectedNodeId || ''); }
function onCtxAddBranch() { openConfigPanel(ctxMenu.value.nodeId || canvasStore.selectedNodeId || ''); }
function onCtxTest(_nodeType: string) { ctxMenu.value.visible = false; }
function onCtxPreviewGate() { ctxMenu.value.visible = false; }
function onCtxEditConditions() { openConfigPanel(ctxMenu.value.nodeId || canvasStore.selectedNodeId || ''); }
function onCtxSetDefaultBranch() { ctxMenu.value.visible = false; }

function handleAddGroup() {
  canvas.addGroup('New Group');
  snapshotYaml();
}

function snapshotYaml() {
  const newYaml = buildYamlFromState();
  yamlContent.value = newYaml;
  snapshot(newYaml);
  canvasStore.setDirty(true);
  emit('yamlChange', newYaml);
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
      // Duplicates inherit the source node's YAML (config etc.) until they have their own.
      ...(existingNodeMap.get(n.id()) ?? existingNodeMap.get(n.data('copiedFrom') as string) ?? {}),
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
