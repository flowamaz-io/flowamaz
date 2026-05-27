<template>
  <div class="h-10 bg-white border-b border-[#E5E7EB] flex items-center px-3 gap-2 shrink-0">
    <!-- Left: actions -->
    <button
      class="toolbar-btn"
      :disabled="!canUndo"
      :title="`Undo (${undoCount} actions available) Ctrl+Z`"
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
    <button class="toolbar-btn" title="Tidy Layout" @click="$emit('tidy')">
      <LayoutDashboard class="w-4 h-4" />
    </button>
    <button class="toolbar-btn" title="Add Group" @click="$emit('addGroup')">
      <Group class="w-4 h-4" />
    </button>

    <!-- Center: workflow name -->
    <div class="flex-1 flex justify-center">
      <input
        :value="workflowName"
        class="bg-transparent text-sm text-gray-700 text-center outline-none border-b border-transparent hover:border-gray-300 focus:border-primary-500 transition-colors w-64 py-0.5"
        placeholder="Workflow name"
        @input="$emit('rename', ($event.target as HTMLInputElement).value)"
      />
    </div>

    <!-- Right: zoom + view -->
    <button class="toolbar-btn" title="Zoom In" @click="$emit('zoomIn')">
      <ZoomIn class="w-4 h-4" />
    </button>
    <button class="toolbar-btn" title="Zoom Out" @click="$emit('zoomOut')">
      <ZoomOut class="w-4 h-4" />
    </button>
    <button class="toolbar-btn" title="Fit to Screen" @click="$emit('fit')">
      <Maximize2 class="w-4 h-4" />
    </button>
    <button
      :class="['toolbar-btn', showMinimap ? 'text-primary-600' : '']"
      title="Toggle Minimap"
      @click="$emit('toggleMinimap')"
    >
      <Map class="w-4 h-4" />
    </button>

    <!-- Save state indicator -->
    <div class="w-px h-5 bg-gray-200 mx-1" />
    <button
      class="toolbar-btn text-xs gap-1.5"
      :class="isDirty ? 'text-amber-400' : 'text-green-400'"
      title="Save Ctrl+S"
      @click="$emit('save')"
    >
      <Save class="w-4 h-4" />
      <span>{{ isDirty ? 'Unsaved' : 'Saved' }}</span>
    </button>
  </div>
</template>

<script setup lang="ts">
import { Undo2, Redo2, LayoutDashboard, Group, ZoomIn, ZoomOut, Maximize2, Map, Save } from 'lucide-vue-next';

defineProps<{
  workflowName: string;
  canUndo: boolean;
  canRedo: boolean;
  undoCount: number;
  isDirty: boolean;
  showMinimap: boolean;
}>();

defineEmits<{
  undo: [];
  redo: [];
  tidy: [];
  addGroup: [];
  zoomIn: [];
  zoomOut: [];
  fit: [];
  toggleMinimap: [];
  save: [];
  rename: [name: string];
}>();
</script>
