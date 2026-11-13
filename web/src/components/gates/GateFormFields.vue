<script setup lang="ts">
import type { GateFormField, GateFormValue } from '@/types';

// The approver's inputs for a gate form. Values live in the parent (v-model); problems come from answerProblems().
const props = defineProps<{
  fields: GateFormField[];
  modelValue: Record<string, GateFormValue>;
  problems: Record<string, string>;
  disabled?: boolean;
}>();

const emit = defineEmits<{ 'update:modelValue': [value: Record<string, GateFormValue>] }>();

function set(key: string, value: GateFormValue): void {
  emit('update:modelValue', { ...props.modelValue, [key]: value });
}

function text(key: string): string {
  const v = props.modelValue[key];
  return typeof v === 'string' || typeof v === 'number' ? String(v) : '';
}

const inputClass = (key: string): string[] => [
  'w-full rounded-md border px-3 py-2 text-sm text-slate-800 focus:outline-none focus:ring-2',
  props.problems[key] ? 'border-red-300 focus:ring-red-100' : 'border-slate-300 focus:border-primary-400 focus:ring-primary-100',
];
</script>

<template>
  <div class="space-y-4">
    <div
      v-for="f in fields"
      :key="f.key"
      :data-testid="`gate-field-${f.key}`"
    >
      <label
        v-if="f.type === 'checkbox'"
        class="flex cursor-pointer items-start gap-2 text-sm text-slate-700"
      >
        <input
          type="checkbox"
          class="mt-0.5 h-4 w-4 rounded border-slate-300 text-primary-600 focus:ring-primary-200"
          :checked="modelValue[f.key] === true"
          :disabled="disabled"
          @change="set(f.key, ($event.target as HTMLInputElement).checked)"
        >
        <span>
          {{ f.label }}<span
            v-if="f.required"
            class="text-red-600"
          > *</span>
        </span>
      </label>
      <template v-else>
        <label
          :for="`gate-field-input-${f.key}`"
          class="mb-1 block text-sm font-medium text-slate-700"
        >
          {{ f.label }}<span
            v-if="f.required"
            class="text-red-600"
          > *</span>
        </label>
        <textarea
          v-if="f.type === 'textarea'"
          :id="`gate-field-input-${f.key}`"
          rows="3"
          :class="inputClass(f.key)"
          :value="text(f.key)"
          :disabled="disabled"
          @input="set(f.key, ($event.target as HTMLTextAreaElement).value)"
        />
        <select
          v-else-if="f.type === 'select'"
          :id="`gate-field-input-${f.key}`"
          :class="inputClass(f.key)"
          :value="text(f.key)"
          :disabled="disabled"
          @change="set(f.key, ($event.target as HTMLSelectElement).value)"
        >
          <option value="">
            Choose…
          </option>
          <option
            v-for="o in f.options ?? []"
            :key="o"
            :value="o"
          >
            {{ o }}
          </option>
        </select>
        <input
          v-else
          :id="`gate-field-input-${f.key}`"
          :type="f.type === 'text' ? 'text' : f.type"
          :min="f.min ?? undefined"
          :max="f.max ?? undefined"
          :step="f.type === 'number' ? 'any' : undefined"
          :class="inputClass(f.key)"
          :value="text(f.key)"
          :disabled="disabled"
          @input="set(f.key, ($event.target as HTMLInputElement).value)"
        >
      </template>
      <p
        v-if="problems[f.key]"
        class="mt-1 text-xs text-red-600"
        role="alert"
      >
        {{ problems[f.key] }}
      </p>
      <p
        v-else-if="f.help"
        class="mt-1 text-xs text-slate-500"
      >
        {{ f.help }}
      </p>
    </div>
  </div>
</template>
