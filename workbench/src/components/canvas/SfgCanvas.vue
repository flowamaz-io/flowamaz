<template>
  <div class="flex flex-col h-full bg-[#F8FAFC] overflow-hidden">
    <!-- Toolbar -->
    <CanvasToolbar
      :workflow-name="workflowName"
      :can-undo="collabActive ? props.collab!.canUndo.value : canvasStore.canUndo"
      :can-redo="collabActive ? props.collab!.canRedo.value : canvasStore.canRedo"
      :undo-count="collabActive ? undefined : canvasStore.undoCount"
      :is-dirty="props.isDirty ?? canvasStore.isDirty"
      :show-minimap="showMinimap"
      :snap-to-grid="snapToGridEnabled"
      :saving="props.saving ?? false"
      :read-only="props.readOnly ?? false"
      :connect-mode="canvas.connectMode.value"
      @toggle-connect="canvas.setConnectMode(!canvas.connectMode.value)"
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
      <NodePalette v-if="!props.readOnly" />

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
          @mousemove="onCollabMouseMove"
          @mouseleave="props.collab?.setCursor(null)"
        />

        <!-- The connection being drawn in Connect mode (an overlay: nothing is added to the graph mid-drag) -->
        <svg
          v-if="canvas.connectLine.value"
          class="pointer-events-none absolute inset-0 h-full w-full"
          data-testid="connect-line"
          aria-hidden="true"
        >
          <line
            :x1="canvas.connectLine.value.x1"
            :y1="canvas.connectLine.value.y1"
            :x2="canvas.connectLine.value.x2"
            :y2="canvas.connectLine.value.y2"
            stroke="#6366F1"
            stroke-width="2"
            stroke-dasharray="6 4"
          />
        </svg>

        <!-- Other people's pointers in a shared editing session -->
        <CollabCursors
          v-if="props.collab"
          :peers="props.collab.peers.value"
          :viewport="canvas.viewport.value"
        />

        <!-- Box-selection hint / multi-selection count -->
        <div
          class="absolute top-3 left-3 text-[11px] text-gray-500 bg-white/90 border border-gray-200 rounded-md px-2 py-1 shadow-sm pointer-events-none"
        >
          <template v-if="canvas.connectMode.value">
            Drag from one node onto another to connect them — Esc to stop
          </template>
          <template v-else-if="canvas.selectedNodeCount.value > 1">
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

      <!-- YAML panel (shows when no node is selected). Not inside the workflow editor, which has its own
           full YAML panel beside the canvas (this one was squeezed in, cut off). -->
      <div
        v-if="yamlContent && props.initialYaml === undefined"
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
      :show-help="!!host.openHelp"
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
      :variables="configVariables"
      :workflow-trigger-type="workflowTriggerType"
      :node-options="workflowNodeOptions"
      @close="configPanel = { open: false, node: null }"
      @save="applyNodeConfig"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue';
import { useEventListener } from '@vueuse/core';
import * as jsYaml from 'js-yaml';
import { useDebounceFn } from '@vueuse/core';
import {
  NodePalette, CanvasToolbar, NodeContextMenu, CanvasMinimap, createCanvasModel, useCanvasHistory, useCytoscapeCanvas,
  useYamlCanvasSync, nodeIdAtRenderedPoint as nodeIdAt, type CanvasModel,
} from '@flowamaz/canvas';
import NodeConfigPanel, { type ConfigNode, type NodeConfigSave } from './NodeConfigPanel.vue';
import CollabCursors from './CollabCursors.vue';
import type { CanvasCollab, SharedPositions } from '../../canvasCollab';
import { useWorkbenchHost } from '../../host';
import { GRID_SIZE, gridDotSpacing, loadSnapPreference, renderedToModel, saveSnapPreference } from '@flowamaz/canvas/canvasMath';
import type { WorkflowVariable } from './shared/VariableAutocomplete.vue';
import { Clipboard } from 'lucide-vue-next';
import { buildCanvasSkeleton, dumpWorkflowYaml, expandYamlAliases, patchWorkflowYaml } from '@flowamaz/canvas/yaml';
import { mergeText } from '@flowamaz/canvas/textMerge';
import { canvasNodeType, readNodePolicies, workflowSection, writeNodePolicies } from '@flowamaz/canvas/workflowGraph';
import type { NodeType } from '@flowamaz/canvas/types';

/**
 * The visual workflow editor: drag steps from the palette, connect them, edit each step's settings, undo/redo, and
 * keep the YAML in step. Loads the workflow with the host's API (`getWorkflow`); the step settings use the API's
 * optional connector, credential and Library-form calls when it has them.
 */
const props = defineProps<{
  /** The workflow to load (with the host API's getWorkflow). Without it the canvas starts empty. */
  workflowId?: string;
  /** Starts from this document instead of loading the saved one (e.g. a just-generated draft). */
  startYaml?: string;
  /** Changing it reloads the saved workflow (e.g. after a save elsewhere). */
  reloadKey?: number;
  /** Shared canvas state (selection, undo history) when the host also reads it; otherwise the canvas keeps its own. */
  state?: CanvasModel;
  initialYaml?: string;
  saving?: boolean;
  /**
   * The host editor's dirty flag (its YAML differs from the last save). When given, the toolbar's
   * "Saved / Unsaved changes" reads it, so the canvas and the editor top bar always agree.
   */
  isDirty?: boolean;
  /** A shared editing session (roadmap B5): shared node positions, other people's cursors and selections, per-person undo. */
  collab?: CanvasCollab | null;
  /** Watching only (no Designer role): nodes can't be moved or edited, and nothing is saved. */
  readOnly?: boolean;
}>();
const emit = defineEmits<{ save: []; yamlChange: [yaml: string] }>();

const host = useWorkbenchHost();
const containerRef = ref<HTMLElement | null>(null);
const canvasStore = props.state ?? createCanvasModel();
const { snapshot, performUndo, performRedo } = useCanvasHistory(canvasStore);
function openArticle(article: string): void { host.openHelp?.(article); }

const workflowName = ref('Untitled Workflow');
const showMinimap = ref(true);
const yamlContent = ref('');
const yamlCopied = ref(false);
const snapToGridEnabled = ref(loadSnapPreference());

const canvas = useCytoscapeCanvas(canvasStore, containerRef, { snapToGrid: snapToGridEnabled });

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
const { syncYamlToCanvas, rememberPosition } = useYamlCanvasSync(canvasStore, () => canvas.cy.value);

const ctxMenu = ref({ visible: false, x: 0, y: 0, nodeId: '', nodeType: '' });

// ── Node config panel (prompt 06-02) ─────────────────────────────────────────
const configPanel = ref<{ open: boolean; node: ConfigNode | null }>({ open: false, node: null });
const configVariables = ref<WorkflowVariable[]>([]);

function buildConfigNode(nodeId: string): ConfigNode | null {
  if (!nodeId) return null;
  let nodeFromYaml: Record<string, unknown> | undefined;
  try {
    const spec = workflowSection(jsYaml.load(yamlContent.value)) ?? {};
    const nodes = (spec.nodes as Array<Record<string, unknown>>) ?? [];
    nodeFromYaml = nodes.find((n) => n.id === nodeId);
  } catch { /* fall back to canvas data */ }
  const cyNode = canvas.cy.value?.getElementById(nodeId);
  // The config panels key on the canvas type ('human-gate'); YAML may spell it 'HumanGate'.
  const rawType = typeof nodeFromYaml?.type === 'string' ? nodeFromYaml.type : undefined;
  const type: string = rawType ? canvasNodeType(rawType) : (cyNode?.data('nodeType') as string | undefined) ?? 'action';
  const label = (nodeFromYaml?.label as string) ?? (cyNode?.data('label') as string) ?? nodeId;
  const config = (nodeFromYaml?.config as Record<string, unknown>) ?? {};
  const policies = nodeFromYaml ? readNodePolicies(nodeFromYaml) : {};
  return {
    id: nodeId, type, label, config,
    retry: policies.retry ?? null, timeout: policies.timeout ?? null, compensate: policies.compensate ?? null,
  };
}

function refreshConfigVariables(): void {
  const vars: WorkflowVariable[] = [];
  try {
    const spec = workflowSection(jsYaml.load(yamlContent.value)) ?? {};
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
  if (props.readOnly) return;
  const node = buildConfigNode(nodeId);
  if (!node) return;
  refreshConfigVariables();
  configPanel.value = { open: true, node };
  ctxMenu.value.visible = false;
}

function applyNodeConfig(payload: NodeConfigSave): void {
  if (props.readOnly) { configPanel.value = { open: false, node: null }; return; }
  const opened = configPanel.value.node;
  // "Save node" with nothing changed must leave the YAML byte-for-byte as it is (no re-dump,
  // no dirty flag, no undo step).
  if (opened && opened.id === payload.id && opened.label === payload.label
    && JSON.stringify(opened.config) === JSON.stringify(payload.config)
    && JSON.stringify(opened.retry ?? null) === JSON.stringify(payload.retry ?? null)
    && JSON.stringify(opened.timeout ?? null) === JSON.stringify(payload.timeout ?? null)
    && JSON.stringify(opened.compensate ?? null) === JSON.stringify(payload.compensate ?? null)) {
    configPanel.value = { open: false, node: null };
    return;
  }
  // Merge the config + label into the YAML (only those lines change; comments stay), then rebuild so the canvas re-renders.
  const nodeType = configPanel.value.node?.type ?? 'action';
  const hasConfig = Object.keys(payload.config ?? {}).length > 0;
  commitCanvasEdit((base) => patchWorkflowYaml(base, (parsed) => {
    const spec = workflowSection(parsed, true);
    const nodes = (spec.nodes as Array<Record<string, unknown>>) ?? (spec.nodes = []);
    const existing = nodes.find((n) => n.id === payload.id);
    if (existing) {
      existing.label = payload.label;
      // An empty config isn't written into a node that had none (no stray `config: {}`).
      if (hasConfig || existing.config !== undefined) existing.config = payload.config;
      // The node-level retry / timeout / compensate blocks, in the documented form (docs/yaml-spec.md).
      writeNodePolicies(existing, { retry: payload.retry, timeout: payload.timeout, compensate: payload.compensate });
    } else {
      const created: Record<string, unknown> = { id: payload.id, type: nodeType, label: payload.label, ...(hasConfig ? { config: payload.config } : {}) };
      writeNodePolicies(created, {
        retry: payload.retry ?? undefined, timeout: payload.timeout ?? undefined, compensate: payload.compensate ?? undefined,
      });
      nodes.push(created);
    }
  }));

  canvas.cy.value?.getElementById(payload.id).data('label', payload.label);
  syncYamlToCanvas(yamlContent.value);
  configPanel.value = { open: false, node: null };
}

/** `startHistory` = first load of this canvas: the loaded document becomes the undo baseline. */
async function loadWorkflow(options: { startHistory?: boolean } = {}) {
  const workflowId = props.workflowId;
  if (!host.api || !workflowId) {
    if (options.startHistory) canvasStore.resetHistory(buildYamlFromState());
    return;
  }
  try {
    const wf = await host.api.getWorkflow(workflowId);
    if (wf.name) workflowName.value = wf.name;
    // Anchors/aliases (`nodes: &o0 []` / `edges: *o0`) would make nodes and edges one shared array.
    const loadedYaml = wf.yamlContent ? expandYamlAliases(wf.yamlContent) : '';
    if (loadedYaml) {
      yamlContent.value = loadedYaml;
      await nextTick();
      syncYamlToCanvas(loadedYaml);
      // Wait for sync debounce to settle, then tidy the layout, then update minimap
      // (the minimap re-renders itself on layoutstop)
      initialLayoutPending = true;
      setTimeout(() => { canvas.runLayout(); }, 350);
    } else {
      initialLayoutPending = false;
    }
    if (options.startHistory) canvasStore.resetHistory(loadedYaml || buildYamlFromState());
    canvasStore.setDirty(false);
  } catch {
    // canvas stays empty if load fails
    if (options.startHistory) canvasStore.resetHistory(buildYamlFromState());
  }
}

watch(() => props.reloadKey ?? 0, (v) => {
  // In a live session the canvas already has the saved document; reloading would re-run the layout.
  if (v > 0 && !collabActive.value) loadWorkflow();
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
  if (next === undefined || next === yamlContent.value) return;
  if (collabActive.value) {
    // Live session: take others' changes at once (no undo snapshot: undo there is per person, via the
    // shared document), so a canvas edit made a moment later starts from them.
    let parsedOk = false;
    try { const d = jsYaml.load(next); parsedOk = !!d && typeof d === 'object'; } catch { parsedOk = false; }
    if (!parsedOk) return;
    yamlContent.value = next;
    syncYamlToCanvas(next);
    return;
  }
  void adoptEditorYaml(next);
});

/**
 * Steps the config panel offers for "on timeout, go to" and the undo step: name and id, without the
 * trigger (a run can't go back to its start).
 */
const workflowNodeOptions = computed<Array<{ id: string; label: string }>>(() => {
  try {
    const nodes = workflowSection(jsYaml.load(yamlContent.value))?.nodes;
    if (!Array.isArray(nodes)) return [];
    return nodes
      .map((n) => n as Record<string, unknown>)
      .filter((n) => typeof n?.id === 'string' && String(n.type ?? '').toLowerCase() !== 'trigger')
      .map((n) => ({ id: n.id as string, label: typeof n.label === 'string' && n.label.trim() ? n.label : (n.id as string) }));
  } catch {
    return [];
  }
});

const workflowTriggerType = computed<string>(() => {
  try {
    const trigger = workflowSection(jsYaml.load(yamlContent.value))?.trigger as Record<string, unknown> | undefined;
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

// Esc leaves Connect mode wherever focus is: the canvas itself rarely has keyboard focus after a drag.
useEventListener(window, 'keydown', (e: KeyboardEvent) => {
  if (e.key === 'Escape' && canvas.connectMode.value) canvas.setConnectMode(false);
});

// Keyboard shortcuts
function onKeydown(e: KeyboardEvent) {
  if (props.readOnly) return; // watching only: no undo, delete or duplicate
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
  // In a shared session, undo steps back through this person's own changes only.
  if (collabActive.value) { props.collab!.undo(); return; }
  const prev = performUndo();
  if (prev !== null) applyHistoryEntry(prev);
}

function handleRedo() {
  if (collabActive.value) { props.collab!.redo(); return; }
  const next = performRedo();
  if (next !== null) applyHistoryEntry(next);
}

// ── Shared editing session (roadmap B5) ──────────────────────────────────────
// Node positions aren't saved in the YAML; in a session they're shared so everyone sees the same
// layout (and cursors point at the same nodes). The first layout of a page defers to positions the
// room already has; an explicit Tidy, a drag or a new node shares its positions.
const collabActive = computed(() => props.collab?.ready.value === true);
let initialLayoutPending = true;

function sharedPositions(): SharedPositions | null {
  return props.collab?.ready.value ? props.collab.positions.value : null;
}

function publishPositions(ids?: string[]): void {
  const map = sharedPositions();
  const cy = canvas.cy.value;
  if (!map || !cy || !props.collab?.canEdit.value) return;
  map.doc?.transact(() => {
    cy.nodes().forEach((n) => {
      if (n.data('isGroup') || (ids && !ids.includes(n.id()))) return;
      const p = n.position();
      const prev = map.get(n.id());
      if (!prev || Math.abs(prev.x - p.x) > 0.5 || Math.abs(prev.y - p.y) > 0.5) map.set(n.id(), { x: p.x, y: p.y });
    });
  });
}

function applySharedPositions(): void {
  const map = sharedPositions();
  const cy = canvas.cy.value;
  if (!map || !cy) return;
  map.forEach((pos, id) => {
    const node = cy.getElementById(id);
    if (node.length > 0 && !node.grabbed()) node.position({ x: pos.x, y: pos.y });
  });
}

let unobservePositions: (() => void) | null = null;
watch(() => props.collab?.positions.value ?? null, (map) => {
  unobservePositions?.();
  unobservePositions = null;
  if (!map) return;
  const observer = (event: { transaction: { local: boolean } }): void => {
    if (!event.transaction.local) applySharedPositions();
  };
  map.observe(observer);
  unobservePositions = () => map.unobserve(observer);
}, { immediate: true });

// Once this browser joins the room: take the room's layout, or share ours if the room has none yet.
watch(collabActive, (active) => {
  if (!active) return;
  const map = sharedPositions();
  if (map && map.size > 0) applySharedPositions();
  else if (!initialLayoutPending) publishPositions();
});

let cursorFrame = 0;
function onCollabMouseMove(e: MouseEvent): void {
  if (!props.collab || cursorFrame) return;
  const rect = containerRef.value?.getBoundingClientRect();
  if (!rect) return;
  const { x, y } = { x: e.clientX - rect.left, y: e.clientY - rect.top };
  cursorFrame = requestAnimationFrame(() => {
    cursorFrame = 0;
    const { zoom, pan } = canvas.viewport.value;
    props.collab?.setCursor(renderedToModel({ x, y }, pan, zoom));
  });
}

watch(() => canvasStore.selectedNodeId, (id) => props.collab?.setSelected(id ?? null));

// Rings in each person's colour on the nodes others have selected.
watch(() => props.collab?.peers.value ?? [], (peers) => {
  const cy = canvas.cy.value;
  if (!cy) return;
  cy.nodes('.collab-selected').removeClass('collab-selected');
  for (const p of peers) {
    if (!p.selected) continue;
    const node = cy.getElementById(p.selected);
    if (node.length > 0) node.data('collabColor', p.user.color).addClass('collab-selected');
  }
});

// Drag-and-drop from palette
function onDrop(e: DragEvent) {
  if (props.readOnly) return;
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
  if (!host.api || !props.workflowId) return;
  try {
    const wf = await host.api.getWorkflow(props.workflowId);
    if (wf.name) workflowName.value = wf.name;
  } catch {
    // Title stays as-is if the workflow can't be loaded.
  }
}

function onContextMenu(e: MouseEvent) {
  if (props.readOnly) return;
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
  // A document the host hands over (e.g. a just-generated draft) takes priority over the saved one.
  const storedYaml = props.startYaml;
  if (storedYaml) {
    yamlContent.value = storedYaml;
    canvasStore.resetHistory(storedYaml);
    await nextTick();
    syncYamlToCanvas(storedYaml);
    initialLayoutPending = false;
    // The NL-generation path skips loadWorkflow(); still resolve the real title from the API
    // so the toolbar shows the workflow name, not "Untitled Workflow".
    void loadWorkflowName();
  } else {
    await loadWorkflow({ startHistory: true });
  }
  canvas.setEditable(!props.readOnly);
  // Clicking or box-selecting on the canvas gives it keyboard focus, so Ctrl/Cmd+Z, Delete and
  // Ctrl/Cmd+D reach onKeydown even when Cytoscape swallows the mousedown.
  canvas.cy.value?.on('tapstart', () => { containerRef.value?.focus({ preventScroll: true }); });
  // Remember where removed nodes sat, so undoing a delete restores them in place.
  canvas.cy.value?.on('remove', 'node', (evt) => { rememberPosition(evt.target.id(), evt.target.position()); });
  // Shared session: the first layout takes the room's positions if it has them; any other layout,
  // drag or new node shares its positions.
  canvas.cy.value?.on('layoutstop', () => {
    const map = sharedPositions();
    if (initialLayoutPending) {
      initialLayoutPending = false;
      if (map && map.size > 0) { applySharedPositions(); return; }
    }
    publishPositions();
  });
  canvas.cy.value?.on('dragfree', 'node', (evt) => { publishPositions([evt.target.id()]); });
  // A connection drawn in Connect mode (useCytoscapeCanvas draws the line as an overlay, so nothing is added to
  // the graph until here): give it a stable id and write it to the YAML.
  canvas.onConnect((from, to) => {
    if (props.readOnly) return;
    if (addEdge(from, to)) snapshotYaml();
  });
  canvas.cy.value?.on('add', 'node', (evt) => {
    const id = evt.target.id();
    setTimeout(() => {
      const map = sharedPositions();
      if (!map || initialLayoutPending) return;
      const pos = map.get(id);
      if (pos) evt.target.position({ x: pos.x, y: pos.y });
      else publishPositions([id]);
    }, 0);
  });
  // Double-click a node to open its configuration panel (prompt 06-02).
  canvas.cy.value?.on('dbltap', 'node', (evt) => {
    const id = evt.target.id();
    if (id) openConfigPanel(id);
  });
  // Dev server only (never in a build): lets the Playwright suite find where a node is drawn, since Cytoscape
  // paints nodes on a <canvas> with no DOM element to target.
  if ((import.meta as { env?: { DEV?: boolean } }).env?.DEV) {
    // Test hook: graph element counts (E2E checks that drawing a connection adds nothing to the graph mid-drag).
    (window as unknown as { __fmzCanvasCounts?: () => { nodes: number; edges: number } }).__fmzCanvasCounts = () => ({
      nodes: canvas.cy.value?.nodes().length ?? 0,
      edges: canvas.cy.value?.edges().length ?? 0,
    });
    (window as unknown as { __fmzCanvasNodeCenter?: (id: string) => { x: number; y: number } | null }).__fmzCanvasNodeCenter = (id) => {
      const node = canvas.cy.value?.getElementById(id);
      const box = containerRef.value?.getBoundingClientRect();
      if (!node || node.empty() || !box) return null;
      const p = node.renderedPosition();
      return { x: box.left + p.x, y: box.top + p.y };
    };
  }
});

watch(() => props.readOnly, (readOnly) => canvas.setEditable(!readOnly), { immediate: true });

onUnmounted(() => {
  unobservePositions?.();
  if (cursorFrame) cancelAnimationFrame(cursorFrame);
  document.removeEventListener('click', closeCtxMenu);
});

function closeCtxMenu() { ctxMenu.value.visible = false; }

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

/** Right-click → "Add connected node": a new step below this one, already connected to it. */
function onCtxAddConnected() {
  ctxMenu.value.visible = false;
  const cy = canvas.cy.value;
  const sourceId = ctxMenu.value.nodeId || canvasStore.selectedNodeId;
  const source = sourceId ? cy?.getElementById(sourceId) : undefined;
  if (!cy || !source || source.empty() || props.readOnly) return;
  const at = source.position();
  const id = canvas.addNodeToCanvas('action', { x: at.x, y: at.y + 120 });
  if (!id) return;
  addEdge(source.id(), id);
  snapshotYaml();
}

/** Adds an edge with a stable id the YAML can keep. False when that connection already exists. */
function addEdge(from: string, to: string): boolean {
  const cy = canvas.cy.value;
  if (!cy) return false;
  const exists = cy.edges().filter((e) => e.source().id() === from && e.target().id() === to).nonempty();
  if (exists) return false;
  let id = `e-${from}-${to}`;
  for (let n = 2; cy.getElementById(id).length > 0; n++) id = `e-${from}-${to}-${n}`;
  cy.add({ group: 'edges', data: { id, source: from, target: to } });
  canvasStore.addEdge({ id, from, to });
  return true;
}

function onCtxHelp() {
  // The node's type as drawn (the right-click read it from the graph). The store's node list only has nodes dropped
  // from the palette, so loaded nodes had no help before.
  const id = ctxMenu.value.nodeId || canvasStore.selectedNodeId;
  const type = ctxMenu.value.nodeType || (id ? (canvas.cy.value?.getElementById(id).data('nodeType') as string | undefined) : undefined);
  if (type) openArticle(`node-types/${canvasNodeType(type)}-nodes`);
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
  commitCanvasEdit((base) => buildYamlFromState(base));
}

/**
 * Apply a canvas edit and hand the result to the editor. `build` turns the canvas's copy of the YAML
 * into the edited YAML. If the editor's document has moved on since the canvas last took it (a
 * collaborator's change, or typing in the YAML panel, that hasn't reached the canvas yet), the edit is
 * merged into the newer document instead of overwriting it.
 */
function commitCanvasEdit(build: (base: string) => string): void {
  if (props.readOnly) return; // watching only: nothing the canvas does changes the document
  const base = yamlContent.value;
  const mine = build(base);
  const latest = props.initialYaml;
  const next = latest !== undefined && latest !== base ? mergeText(base, mine, latest) : mine;
  if (next === base && next === latest) return;
  yamlContent.value = next;
  snapshot(next);
  canvasStore.setDirty(true);
  emit('yamlChange', next);
  if (next !== mine) syncYamlToCanvas(next);
}

function buildYamlFromState(base: string = yamlContent.value): string {
  const cy = canvas.cy.value;

  // Start from the current YAML to preserve all metadata (trigger, variables, node configs) and its
  // comments and formatting. Only the nodes and edges that changed are rewritten.
  let parseable = false;
  if (base) {
    try {
      const existing = jsYaml.load(base);
      parseable = !!existing && typeof existing === 'object';
    } catch { /* fall back to blank scaffold */ }
  }
  if (!parseable) {
    const skeleton = buildCanvasSkeleton(workflowName.value);
    applyCanvasGraph(skeleton, cy);
    return dumpWorkflowYaml(skeleton);
  }
  return patchWorkflowYaml(base, (parsed) => applyCanvasGraph(parsed, cy));
}

/** Canvas node type → the engine's spelling (backend NodeType), for documents written that way. */
const ENGINE_TYPE_NAMES: Record<string, string> = {
  trigger: 'Trigger', action: 'Action', ai: 'Ai', 'human-gate': 'HumanGate', router: 'Router', end: 'End',
  foreach: 'ForEach', parallel: 'Parallel', 'try-catch': 'TryCatch', wait: 'Wait', 'sub-workflow': 'SubWorkflow',
};

/** Writes the canvas's nodes and edges into a parsed workflow document (other content untouched). */
function applyCanvasGraph(parsed: Record<string, unknown>, cy: typeof canvas.cy.value): void {
  // Short-shape documents (top-level nodes:) are written back in place, never into a second spec.nodes.
  const spec = workflowSection(parsed, true);
  const existingNodes = (spec.nodes as Array<Record<string, unknown>>) ?? [];
  const existingEdges = (spec.edges as Array<Record<string, unknown>>) ?? [];
  const existingNodeMap = new Map(existingNodes.map(n => [n.id as string, n]));
  // Edges without an id get `edge-<index>` on the canvas (workflowGraph.util); match them the same way.
  const existingEdgeMap = new Map(existingEdges.map((e, index) => [(e.id as string | undefined) ?? `edge-${index}`, e]));
  /** Keep what the YAML says when the canvas only shows it differently (type spelling, missing label/id). */
  // New nodes follow the document's spelling: if its types are written like the engine's (`Action`, `HumanGate`), so are theirs.
  const engineSpelling = existingNodes.some((n) => typeof n.type === 'string' && /^[A-Z]/.test(n.type));
  const nodeFields = (id: string, type: string, label: string, existing?: Record<string, unknown>) => ({
    id,
    type: existing && canvasNodeType(existing.type as string | undefined) === canvasNodeType(type)
      ? existing.type
      : engineSpelling ? ENGINE_TYPE_NAMES[type] ?? type : type,
    ...(existing && existing.label === undefined && label === id ? {} : { label }),
  });
  const edgeId = (id: string, existing?: Record<string, unknown>) =>
    (existing && existing.id === undefined && /^edge-\d+$/.test(id) ? {} : { id });

  if (cy) {
    // Use Cytoscape as source of truth — includes pre-loaded nodes and newly-added nodes
    spec.nodes = cy.nodes().filter(n => !n.data('isGroup') && n.id() !== '').map(n => ({
      // Duplicates inherit the source node's YAML (config etc.) until they have their own.
      ...(existingNodeMap.get(n.id()) ?? existingNodeMap.get(n.data('copiedFrom') as string) ?? {}),
      ...nodeFields(n.id(), n.data('nodeType') || 'action', n.data('label') || n.id(), existingNodeMap.get(n.id())),
    }));

    spec.edges = cy.edges().map(e => ({
      ...(existingEdgeMap.get(e.id()) ?? {}),
      ...edgeId(e.id(), existingEdgeMap.get(e.id())),
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
}
</script>
