<script setup lang="ts">
import { computed } from 'vue';

type Config = Record<string, unknown>;

const props = defineProps<{ config: Config }>();
const emit = defineEmits<{ update: [config: Config] }>();

const OUTCOMES = ['success', 'failure', 'custom'] as const;

const outcome = computed<string>(() => (props.config.outcome as string) ?? 'success');
const description = computed<string>(() => (props.config.outcomeDescription as string) ?? '');

function patch(updates: Config): void {
  emit('update', { ...props.config, ...updates });
}
</script>

<template>
  <div class="space-y-4">
    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">Outcome</label>
      <select
        :value="outcome"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm capitalize text-gray-100 focus:border-indigo-400 focus:outline-none"
        @change="patch({ outcome: ($event.target as HTMLSelectElement).value })"
      >
        <option
          v-for="o in OUTCOMES"
          :key="o"
          :value="o"
        >
          {{ o }}
        </option>
      </select>
    </div>
    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">Outcome description</label>
      <textarea
        :value="description"
        rows="2"
        placeholder="What this end state means for the run."
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
        @input="patch({ outcomeDescription: ($event.target as HTMLTextAreaElement).value })"
      />
    </div>
  </div>
</template>
