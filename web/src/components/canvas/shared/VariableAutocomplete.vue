<script setup lang="ts">
import { computed, ref } from 'vue';

export interface WorkflowVariable {
  name: string;
  type: string;
}

const props = withDefaults(
  defineProps<{
    modelValue: string;
    variables: WorkflowVariable[];
    label?: string;
    placeholder?: string;
    multiline?: boolean;
  }>(),
  { label: '', placeholder: '', multiline: false },
);

const emit = defineEmits<{ 'update:modelValue': [value: string] }>();

const focused = ref(false);

// The "open" interpolation fragment is the text after the last `${` that has not
// yet been closed by a `}`. When present we offer matching variables.
const openFragment = computed<string | null>(() => {
  const value = props.modelValue;
  const open = value.lastIndexOf('${');
  if (open === -1) return null;
  const after = value.slice(open + 2);
  if (after.includes('}')) return null;
  return after;
});

const suggestions = computed<WorkflowVariable[]>(() => {
  const fragment = openFragment.value;
  if (fragment === null) return [];
  const needle = fragment.toLowerCase();
  return props.variables.filter((v) => v.name.toLowerCase().includes(needle));
});

const showDropdown = computed(() => focused.value && suggestions.value.length > 0);

function onInput(event: Event): void {
  emit('update:modelValue', (event.target as HTMLInputElement | HTMLTextAreaElement).value);
}

function insert(variable: WorkflowVariable): void {
  const value = props.modelValue;
  const open = value.lastIndexOf('${');
  const next = `${value.slice(0, open)}\${${variable.name}}`;
  emit('update:modelValue', next);
}
</script>

<template>
  <div class="relative">
    <label
      v-if="label"
      class="mb-1 block text-xs font-medium text-gray-300"
    >{{ label }}</label>
    <textarea
      v-if="multiline"
      :value="modelValue"
      :placeholder="placeholder"
      rows="3"
      class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
      @input="onInput"
      @focus="focused = true"
      @blur="focused = false"
    />
    <input
      v-else
      :value="modelValue"
      :placeholder="placeholder"
      type="text"
      class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
      @input="onInput"
      @focus="focused = true"
      @blur="focused = false"
    >
    <ul
      v-if="showDropdown"
      class="absolute z-50 mt-1 max-h-40 w-full overflow-y-auto rounded-md border border-gray-600 bg-gray-900 py-1 shadow-lg"
    >
      <li
        v-for="variable in suggestions"
        :key="variable.name"
        class="flex cursor-pointer items-center justify-between px-2 py-1 text-sm text-gray-200 hover:bg-gray-700"
        data-test="variable-suggestion"
        @mousedown.prevent="insert(variable)"
      >
        <span class="font-mono">{{ variable.name }}</span>
        <span class="text-xs text-gray-500">{{ variable.type }}</span>
      </li>
    </ul>
  </div>
</template>
