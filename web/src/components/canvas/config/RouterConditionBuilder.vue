<script setup lang="ts">
import { computed } from 'vue';
import { Plus, Trash2 } from 'lucide-vue-next';
import VariableAutocomplete, { type WorkflowVariable } from '../shared/VariableAutocomplete.vue';

type Config = Record<string, unknown>;

export interface RouterBranch {
  label: string;
  condition: string;
  isDefault?: boolean;
}

const props = defineProps<{ config: Config; variables: WorkflowVariable[] }>();
const emit = defineEmits<{ update: [config: Config] }>();

const branches = computed<RouterBranch[]>(() => (props.config.branches as RouterBranch[]) ?? []);

function setBranches(next: RouterBranch[]): void {
  emit('update', { ...props.config, branches: next });
}

function addBranch(): void {
  setBranches([...branches.value, { label: `Branch ${branches.value.length + 1}`, condition: '' }]);
}

function removeBranch(index: number): void {
  setBranches(branches.value.filter((_, i) => i !== index));
}

function setLabel(index: number, label: string): void {
  setBranches(branches.value.map((b, i) => (i === index ? { ...b, label } : b)));
}

function setCondition(index: number, condition: string): void {
  setBranches(branches.value.map((b, i) => (i === index ? { ...b, condition } : b)));
}

const hasDefault = computed(() => branches.value.some((b) => b.isDefault));

function toggleDefault(): void {
  if (hasDefault.value) {
    setBranches(branches.value.map((b) => ({ ...b, isDefault: false })));
  } else {
    setBranches([...branches.value, { label: 'Default', condition: '', isDefault: true }]);
  }
}

// Exposed for unit testing the branch-builder logic in isolation.
defineExpose({ addBranch, branches });
</script>

<template>
  <div class="space-y-3">
    <p class="text-xs text-gray-500">
      Each branch routes the workflow when its condition is true. Use <code>${'{'}variable{'}'}</code> in expressions.
    </p>
    <div
      v-for="(branch, index) in branches"
      :key="index"
      class="space-y-1.5 rounded-md border border-gray-700 p-2"
    >
      <div class="flex items-center gap-2">
        <input
          :value="branch.label"
          type="text"
          placeholder="Branch label"
          class="flex-1 rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
          @input="setLabel(index, ($event.target as HTMLInputElement).value)"
        >
        <button
          type="button"
          class="rounded p-1 text-gray-500 hover:bg-gray-700 hover:text-red-400"
          aria-label="Remove branch"
          @click="removeBranch(index)"
        >
          <Trash2 class="h-4 w-4" />
        </button>
      </div>
      <VariableAutocomplete
        v-if="!branch.isDefault"
        :model-value="branch.condition"
        :variables="variables"
        placeholder="e.g. ${amount} > 1000"
        @update:model-value="setCondition(index, $event)"
      />
      <p
        v-else
        class="text-xs text-amber-400"
      >
        Catch-all — runs when no other branch matches.
      </p>
    </div>

    <button
      type="button"
      class="flex items-center gap-1 text-xs font-medium text-indigo-400 hover:text-indigo-300"
      data-test="add-branch"
      @click="addBranch"
    >
      <Plus class="h-3.5 w-3.5" />
      Add branch
    </button>

    <label class="flex items-center gap-2 text-sm text-gray-200">
      <input
        type="checkbox"
        :checked="hasDefault"
        class="h-4 w-4 rounded border-gray-600 bg-gray-800 text-indigo-500"
        @change="toggleDefault"
      >
      Include a default (catch-all) branch
    </label>
  </div>
</template>
