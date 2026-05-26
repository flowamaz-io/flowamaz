<template>
  <div class="space-y-4">
    <div class="text-sm text-gray-300 font-medium">
      Review detected elements with low confidence (below 80%)
    </div>

    <div
      v-for="el in elements"
      :key="el.id"
      class="p-3 bg-gray-800 border border-gray-700 rounded space-y-2"
    >
      <div class="flex items-center gap-2">
        <span class="text-xs px-2 py-0.5 rounded bg-yellow-900 text-yellow-300 font-mono">{{ (el.confidence * 100).toFixed(0) }}%</span>
        <span class="text-sm text-gray-200 font-medium">{{ el.label }}</span>
        <span class="text-xs text-gray-500">Detected as: {{ el.detectedType }}</span>
      </div>
      <div class="text-xs text-gray-400">Is this correct?</div>
      <div class="flex flex-wrap gap-2">
        <button
          class="px-2 py-1 text-xs rounded bg-indigo-600 hover:bg-indigo-500 text-white transition-colors"
          @click="confirm(el.id, el.detectedType)"
        >
          Yes, {{ el.detectedType }}
        </button>
        <button
          v-for="alt in alternatives(el.detectedType)"
          :key="alt"
          class="px-2 py-1 text-xs rounded bg-gray-700 hover:bg-gray-600 text-gray-300 transition-colors"
          @click="confirm(el.id, alt)"
        >
          Change to: {{ alt }}
        </button>
      </div>
    </div>

    <div v-if="allConfirmed" class="text-sm text-green-400">
      All elements confirmed. Click "Apply to Canvas" to proceed.
    </div>

    <button
      :disabled="!allConfirmed"
      class="w-full py-2 px-4 rounded bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 disabled:cursor-not-allowed text-sm font-medium text-white transition-colors"
      @click="$emit('apply')"
    >
      Apply to Canvas
    </button>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';

interface LowConfidenceElement {
  id: string;
  label: string;
  detectedType: string;
  confidence: number;
  annotations: string[];
}

const props = defineProps<{ elements: LowConfidenceElement[] }>();
const emit = defineEmits<{ apply: []; confirmed: [corrections: Record<string, string>] }>();

const corrections = ref<Record<string, string>>({});

const allConfirmed = computed(() =>
  props.elements.every(el => corrections.value[el.id] !== undefined)
);

const allTypes = ['action', 'trigger', 'router', 'human-gate', 'end', 'ai', 'wait', 'foreach', 'parallel', 'annotation'];

function alternatives(detectedType: string) {
  return allTypes.filter(t => t !== detectedType).slice(0, 4);
}

function confirm(id: string, type: string) {
  corrections.value = { ...corrections.value, [id]: type };
  if (allConfirmed.value) emit('confirmed', corrections.value);
}
</script>
