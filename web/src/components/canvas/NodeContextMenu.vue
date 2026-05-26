<template>
  <Teleport to="body">
    <div
      v-if="visible"
      class="fixed z-50 bg-gray-800 border border-gray-600 rounded-lg shadow-xl py-1 min-w-44"
      :style="{ top: `${y}px`, left: `${x}px` }"
      @click.stop
    >
      <button class="ctx-item" @click="emit('edit')">
        <Edit2 class="w-4 h-4" />
        <span>Edit</span>
      </button>
      <button class="ctx-item" @click="emit('duplicate')">
        <Copy class="w-4 h-4" />
        <span>Duplicate</span>
      </button>
      <button class="ctx-item" @click="emit('addConnected')">
        <Plus class="w-4 h-4" />
        <span>Add connected node</span>
        <ChevronRight class="w-3 h-3 ml-auto" />
      </button>
      <button class="ctx-item" @click="emit('help')">
        <HelpCircle class="w-4 h-4" />
        <span>Help with this node type</span>
      </button>
      <div class="border-t border-gray-700 my-1" />
      <button class="ctx-item text-red-400 hover:text-red-300" @click="emit('delete')">
        <Trash2 class="w-4 h-4" />
        <span>Delete</span>
      </button>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { Edit2, Copy, Plus, ChevronRight, HelpCircle, Trash2 } from 'lucide-vue-next';

defineProps<{ visible: boolean; x: number; y: number }>();
const emit = defineEmits<{
  edit: [];
  duplicate: [];
  addConnected: [];
  help: [];
  delete: [];
}>();
</script>
