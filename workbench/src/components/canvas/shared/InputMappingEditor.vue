<script setup lang="ts">
import { Plus, Trash2 } from 'lucide-vue-next';
import VariableAutocomplete, { type WorkflowVariable } from './VariableAutocomplete.vue';

export interface Mapping {
  field: string;
  value: string;
}

const props = withDefaults(
  defineProps<{
    modelValue: Mapping[];
    variables: WorkflowVariable[];
    fieldLabel?: string;
    valueLabel?: string;
    fieldPlaceholder?: string;
  }>(),
  { fieldLabel: 'Field', valueLabel: 'Value', fieldPlaceholder: 'field name' },
);

const emit = defineEmits<{ 'update:modelValue': [value: Mapping[]] }>();

function update(rows: Mapping[]): void {
  emit('update:modelValue', rows);
}

function addRow(): void {
  update([...props.modelValue, { field: '', value: '' }]);
}

function removeRow(index: number): void {
  update(props.modelValue.filter((_, i) => i !== index));
}

function setField(index: number, field: string): void {
  update(props.modelValue.map((m, i) => (i === index ? { ...m, field } : m)));
}

function setValue(index: number, value: string): void {
  update(props.modelValue.map((m, i) => (i === index ? { ...m, value } : m)));
}
</script>

<template>
  <div class="space-y-2">
    <div
      class="grid grid-cols-[1fr_1fr_auto] items-center gap-2 text-xs font-medium text-gray-400"
    >
      <span>{{ fieldLabel }}</span>
      <span>{{ valueLabel }}</span>
      <span class="w-6" />
    </div>
    <p
      v-if="modelValue.length === 0"
      class="rounded-md border border-dashed border-gray-700 px-3 py-3 text-center text-xs text-gray-500"
    >
      No mappings yet. Add one to wire a workflow variable into this node.
    </p>
    <div
      v-for="(row, index) in modelValue"
      :key="index"
      class="grid grid-cols-[1fr_1fr_auto] items-start gap-2"
    >
      <input
        :value="row.field"
        type="text"
        :placeholder="fieldPlaceholder"
        class="rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
        @input="setField(index, ($event.target as HTMLInputElement).value)"
      >
      <VariableAutocomplete
        :model-value="row.value"
        :variables="variables"
        placeholder="value or ${variable}"
        @update:model-value="setValue(index, $event)"
      />
      <button
        type="button"
        class="mt-1.5 rounded p-1 text-gray-500 hover:bg-gray-700 hover:text-red-400"
        aria-label="Remove mapping"
        @click="removeRow(index)"
      >
        <Trash2 class="h-4 w-4" />
      </button>
    </div>
    <button
      type="button"
      class="flex items-center gap-1 text-xs font-medium text-indigo-400 hover:text-indigo-300"
      @click="addRow"
    >
      <Plus class="h-3.5 w-3.5" />
      Add mapping
    </button>
  </div>
</template>
