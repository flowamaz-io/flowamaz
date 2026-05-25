<script setup lang="ts">
import { computed, useId } from 'vue';
import FmSpinner from './FmSpinner.vue';
import HelpTooltip from '@/components/help/HelpTooltip.vue';

const props = withDefaults(
  defineProps<{
    modelValue: string;
    label?: string;
    type?: string;
    placeholder?: string;
    hint?: string;
    error?: string;
    disabled?: boolean;
    loading?: boolean;
    autocomplete?: string;
    /** Field description shown in the inline ? HelpTooltip. */
    help?: string;
    /** Help article slug opened from the tooltip "Learn more" link. */
    helpArticle?: string;
    /** Optional prefix shown inside the field (e.g. a slug base). */
    prefix?: string;
  }>(),
  { type: 'text', disabled: false, loading: false },
);

const emit = defineEmits<{ 'update:modelValue': [value: string] }>();

const id = useId();

const inputClass = computed(() => [
  'w-full rounded-lg border bg-white px-3 py-2 text-sm text-slate-900 shadow-sm transition-colors',
  'placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-offset-0',
  props.error
    ? 'border-danger-400 focus:border-danger-500 focus:ring-danger-200'
    : 'border-slate-300 focus:border-primary-500 focus:ring-primary-200',
  props.disabled ? 'cursor-not-allowed bg-slate-50 text-slate-500' : '',
  props.loading ? 'pr-9' : '',
  props.prefix ? 'rounded-l-none' : '',
]);

function onInput(event: Event): void {
  emit('update:modelValue', (event.target as HTMLInputElement).value);
}
</script>

<template>
  <div>
    <label
      v-if="label"
      :for="id"
      class="mb-1 flex items-center gap-1 text-sm font-medium text-slate-700"
    >
      <span>{{ label }}</span>
      <HelpTooltip
        v-if="help"
        :description="help"
        :article="helpArticle"
      />
    </label>
    <div class="relative flex">
      <span
        v-if="prefix"
        class="inline-flex items-center rounded-l-lg border border-r-0 border-slate-300 bg-slate-50 px-3 text-sm text-slate-500"
      >
        {{ prefix }}
      </span>
      <input
        :id="id"
        :type="type"
        :value="modelValue"
        :placeholder="placeholder"
        :disabled="disabled || loading"
        :autocomplete="autocomplete"
        :class="inputClass"
        @input="onInput"
      >
      <span
        v-if="loading"
        class="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-slate-400"
      >
        <FmSpinner size="sm" />
      </span>
    </div>
    <p
      v-if="error"
      class="mt-1 text-sm text-danger-600"
    >
      {{ error }}
    </p>
    <p
      v-else-if="hint"
      class="mt-1 text-xs text-slate-500"
    >
      {{ hint }}
    </p>
  </div>
</template>
