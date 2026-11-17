<script setup lang="ts">
import { computed } from 'vue';

import { type Config, readString, writeAlias } from './configKeys';

const props = defineProps<{ config: Config }>();
const emit = defineEmits<{ update: [config: Config] }>();

const OUTCOMES = ['success', 'failure', 'custom'] as const;
const DESCRIPTION_KEYS = ['outcome_description', 'outcomeDescription'] as const;

const outcome = computed<string>(() => readString(props.config, ['outcome']) ?? 'success');
// An outcome set in YAML that isn't in the list stays visible instead of reading as "success".
const outcomeOptions = computed<string[]>(() =>
  (OUTCOMES as readonly string[]).includes(outcome.value) ? [...OUTCOMES] : [...OUTCOMES, outcome.value],
);
const description = computed<string>(() => readString(props.config, DESCRIPTION_KEYS) ?? '');

function patch(updates: Config): void {
  emit('update', { ...props.config, ...updates });
}

function setDescription(value: string): void {
  emit('update', writeAlias(props.config, DESCRIPTION_KEYS, 'outcomeDescription', value));
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
          v-for="o in outcomeOptions"
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
        @input="setDescription(($event.target as HTMLTextAreaElement).value)"
      />
    </div>
  </div>
</template>
