<template>
  <div class="h-10 bg-white border-b border-[#E5E7EB] flex items-center px-3 gap-2 shrink-0">
    <!-- Left: actions (not for people who are only watching a live session) -->
    <template v-if="!readOnly">
      <button
        class="toolbar-btn"
        :disabled="!canUndo"
        :title="undoCount === undefined ? 'Undo your last change (Ctrl+Z)' : `Undo (${undoCount} actions available) Ctrl+Z`"
        @click="$emit('undo')"
      >
        <Undo2 class="w-4 h-4" />
      </button>
      <button
        class="toolbar-btn"
        :disabled="!canRedo"
        title="Redo Ctrl+Y"
        @click="$emit('redo')"
      >
        <Redo2 class="w-4 h-4" />
      </button>
      <div class="w-px h-5 bg-gray-200 mx-1" />
      <button
        class="toolbar-btn"
        title="Tidy Layout"
        @click="$emit('tidy')"
      >
        <LayoutDashboard class="w-4 h-4" />
      </button>
      <button
        class="toolbar-btn"
        title="Add Group"
        @click="$emit('addGroup')"
      >
        <Group class="w-4 h-4" />
      </button>
      <button
        class="toolbar-btn"
        :class="connectMode ? 'bg-primary-100 text-primary-700' : ''"
        :aria-pressed="connectMode"
        title="Connect nodes: drag from one node onto another (Esc to stop)"
        data-testid="canvas-connect"
        @click="$emit('toggleConnect')"
      >
        <Spline class="w-4 h-4" />
      </button>
    </template>

    <!-- Center: workflow name -->
    <div class="flex-1 flex justify-center">
      <input
        :value="workflowName"
        class="bg-transparent text-sm text-gray-700 text-center outline-none border-b border-transparent hover:border-gray-300 focus:border-primary-500 transition-colors w-64 py-0.5"
        placeholder="Workflow name"
        :readonly="readOnly"
        @input="$emit('rename', ($event.target as HTMLInputElement).value)"
      >
    </div>

    <!-- Right: zoom + view -->
    <button
      class="toolbar-btn"
      title="Zoom In"
      @click="$emit('zoomIn')"
    >
      <ZoomIn class="w-4 h-4" />
    </button>
    <button
      class="toolbar-btn"
      title="Zoom Out"
      @click="$emit('zoomOut')"
    >
      <ZoomOut class="w-4 h-4" />
    </button>
    <button
      class="toolbar-btn"
      title="Fit to Screen"
      @click="$emit('fit')"
    >
      <Maximize2 class="w-4 h-4" />
    </button>
    <button
      :class="['toolbar-btn', showMinimap ? 'text-primary-600' : '']"
      title="Toggle Minimap"
      @click="$emit('toggleMinimap')"
    >
      <Map class="w-4 h-4" />
    </button>
    <button
      :class="['toolbar-btn', snapToGrid ? 'text-primary-600 bg-primary-50' : '']"
      :title="snapToGrid ? 'Snap to grid: on (20px)' : 'Snap to grid: off'"
      :aria-pressed="snapToGrid"
      @click="$emit('toggleSnap')"
    >
      <Grid3x3 class="w-4 h-4" />
    </button>

    <!-- Save state indicator -->
    <div class="w-px h-5 bg-gray-200 mx-1" />
    <span
      v-if="readOnly"
      class="text-xs text-gray-500"
      data-testid="canvas-watching"
    >Watching — read-only</span>
    <button
      v-else
      class="toolbar-btn text-xs gap-1.5"
      :class="saveButtonClass"
      title="Save Ctrl+S"
      :disabled="!props.isDirty || props.saving"
      @click="onSaveClick"
    >
      <Save class="w-4 h-4" />
      <span>{{ saveLabel }}</span>
    </button>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { Undo2, Redo2, LayoutDashboard, Group, Spline, ZoomIn, ZoomOut, Maximize2, Map, Save, Grid3x3 } from 'lucide-vue-next';

const props = defineProps<{
  workflowName: string;
  canUndo: boolean;
  canRedo: boolean;
  /** Omitted in a shared editing session, where undo covers only your own changes. */
  undoCount?: number;
  isDirty: boolean;
  showMinimap: boolean;
  snapToGrid?: boolean;
  saving?: boolean;
  /** Watching a live session without edit rights: no undo, layout, group or save. */
  readOnly?: boolean;
  /** Connect mode is on (dragging between nodes draws an edge). */
  connectMode?: boolean;
}>();

const emit = defineEmits<{
  undo: [];
  redo: [];
  tidy: [];
  addGroup: [];
  toggleConnect: [];
  zoomIn: [];
  zoomOut: [];
  fit: [];
  toggleMinimap: [];
  toggleSnap: [];
  save: [];
  rename: [name: string];
}>();

const saveLabel = computed(() => {
  if (props.saving) return 'Saving...';
  if (props.isDirty) return 'Unsaved changes';
  return 'Saved';
});

const saveButtonClass = computed(() => {
  if (props.isDirty && !props.saving) return 'text-amber-600 hover:text-amber-700 cursor-pointer';
  return 'text-gray-400 cursor-default';
});

function onSaveClick() {
  if (props.isDirty && !props.saving) emit('save');
}
</script>
