<script setup lang="ts">
import { computed } from 'vue';
import VariableAutocomplete, { type WorkflowVariable } from '../shared/VariableAutocomplete.vue';

import { type Config, readNumber, readString, writeAlias } from './configKeys';

const props = defineProps<{ config: Config; variables: WorkflowVariable[] }>();
const emit = defineEmits<{ update: [config: Config] }>();

const PROVIDERS = ['Anthropic', 'Google', 'BYOM'] as const;
const MODELS: Record<string, string[]> = {
  Anthropic: ['claude-haiku-4-5', 'claude-sonnet-4-6', 'claude-opus-4-8'],
  Google: ['gemini-2-0-flash', 'gemini-1-5-pro'],
  BYOM: ['custom'],
};

// Key aliases in AiNodeWorker's precedence order (spec snake_case first, canvas camelCase second).
const KEYS = {
  systemPrompt: ['system_prompt', 'systemPrompt'],
  userPrompt: ['prompt_template', 'userPrompt', 'prompt'],
  maxTokens: ['max_tokens', 'maxTokens'],
} as const;

const rawProvider = computed<string>(() => readString(props.config, ['provider']) ?? '');
// The engine compares providers case-insensitively; show the matching entry, or the raw value.
const provider = computed<string>(() => {
  if (!rawProvider.value) return 'Anthropic';
  return PROVIDERS.find((p) => p.toLowerCase() === rawProvider.value.toLowerCase()) ?? rawProvider.value;
});
const providerOptions = computed<string[]>(() =>
  (PROVIDERS as readonly string[]).includes(provider.value) ? [...PROVIDERS] : [...PROVIDERS, provider.value],
);
const model = computed<string>(() => readString(props.config, ['model']) ?? '');
const systemPrompt = computed<string>(() => readString(props.config, KEYS.systemPrompt) ?? '');
const userPrompt = computed<string>(() => readString(props.config, KEYS.userPrompt) ?? '');
const maxTokens = computed<number>(() => readNumber(props.config, KEYS.maxTokens) ?? 1024);
const outputVariable = computed<string>(() => (props.config.outputVariable as string) ?? '');

function patch(updates: Config): void {
  emit('update', { ...props.config, ...updates });
}

/** Store a setting under the alias already in use, so a YAML-authored key is edited in place. */
function patchAlias(keys: readonly string[], fallbackKey: string, value: unknown): void {
  emit('update', writeAlias(props.config, keys, fallbackKey, value));
}

// A model set in YAML that isn't in the curated list stays selectable instead of showing blank.
const models = computed(() => {
  const list = MODELS[provider.value] ?? [];
  return model.value && !list.includes(model.value) ? [...list, model.value] : list;
});
</script>

<template>
  <div class="space-y-4">
    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">Provider</label>
      <select
        :value="provider"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
        @change="patch({ provider: ($event.target as HTMLSelectElement).value, model: '' })"
      >
        <option
          v-for="p in providerOptions"
          :key="p"
          :value="p"
        >
          {{ p }}
        </option>
      </select>
    </div>

    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">Model</label>
      <select
        :value="model"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
        @change="patch({ model: ($event.target as HTMLSelectElement).value })"
      >
        <option value="">
          Select a model
        </option>
        <option
          v-for="m in models"
          :key="m"
          :value="m"
        >
          {{ m }}
        </option>
      </select>
    </div>

    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">System prompt</label>
      <textarea
        :value="systemPrompt"
        rows="3"
        placeholder="You are a helpful assistant…"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
        @input="patchAlias(KEYS.systemPrompt, 'systemPrompt', ($event.target as HTMLTextAreaElement).value)"
      />
    </div>

    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">User prompt template</label>
      <VariableAutocomplete
        :model-value="userPrompt"
        :variables="variables"
        multiline
        placeholder="Summarise {{ invoice_data }}…"
        @update:model-value="patchAlias(KEYS.userPrompt, 'userPrompt', $event)"
      />
    </div>

    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">
        Max tokens: <span class="text-gray-100">{{ maxTokens }}</span>
      </label>
      <input
        :value="maxTokens"
        type="range"
        min="256"
        max="8192"
        step="256"
        class="w-full accent-indigo-500"
        @input="patchAlias(KEYS.maxTokens, 'maxTokens', Number(($event.target as HTMLInputElement).value))"
      >
    </div>

    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">Output variable</label>
      <input
        :value="outputVariable"
        type="text"
        placeholder="ai_response"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm font-mono text-gray-100 focus:border-indigo-400 focus:outline-none"
        @input="patch({ outputVariable: ($event.target as HTMLInputElement).value })"
      >
    </div>
  </div>
</template>
