<script setup lang="ts">
import { ref, watch } from 'vue';
import FmButton from './FmButton.vue';
import FmModal from './FmModal.vue';
import { describeError } from '../api';
import { useWorkbenchApi } from '../host';
import { slugify } from '../strings';
import { starterWorkflowYaml } from '../workflows';
import type { WorkflowDefinitionResponse } from '../types';

// Create a workflow from a name. It starts from a small workflow that publishes as it is, to edit from there.
const props = defineProps<{ modelValue: boolean }>();
const emit = defineEmits<{ 'update:modelValue': [open: boolean]; created: [workflow: WorkflowDefinitionResponse] }>();

const api = useWorkbenchApi();
const name = ref('');
const slug = ref('');
const slugEdited = ref(false);
const problem = ref<string | null>(null);
const submitting = ref(false);

watch(() => props.modelValue, (open) => {
  if (!open) return;
  name.value = '';
  slug.value = '';
  slugEdited.value = false;
  problem.value = null;
});
watch(name, (value) => { if (!slugEdited.value) slug.value = slugify(value); });

async function submit(): Promise<void> {
  problem.value = null;
  if (!name.value.trim()) { problem.value = 'Give the workflow a name, e.g. "Leave request".'; return; }
  if (!/^[a-z0-9]+(-[a-z0-9]+)*$/.test(slug.value)) {
    problem.value = 'The short id can use lower-case letters, digits and single dashes, e.g. "leave-request".';
    return;
  }
  submitting.value = true;
  try {
    const created = await api.createWorkflow({
      name: name.value.trim(), slug: slug.value, yamlContent: starterWorkflowYaml(name.value.trim(), slug.value),
      createdByMethod: 'Canvas',
    });
    emit('created', created);
    emit('update:modelValue', false);
  } catch (e) {
    problem.value = describeError(e).message;
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <FmModal
    :model-value="modelValue"
    title="New workflow"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div class="space-y-4">
      <div>
        <label
          for="new-workflow-name"
          class="mb-1 block text-sm font-medium text-slate-700"
        >Name</label>
        <input
          id="new-workflow-name"
          v-model="name"
          type="text"
          placeholder="Leave request"
          class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
        >
      </div>
      <div>
        <label
          for="new-workflow-slug"
          class="mb-1 block text-sm font-medium text-slate-700"
        >Short id</label>
        <input
          id="new-workflow-slug"
          v-model="slug"
          type="text"
          class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 font-mono text-sm text-slate-900 focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
          @input="slugEdited = true"
        >
        <p class="mt-1 text-xs text-slate-500">
          Unique in the workspace; used in the webhook address and the CLI.
        </p>
      </div>
      <p class="text-xs text-slate-500">
        The workflow starts as a draft with a trigger, one approval and an end. Edit its YAML, then publish.
      </p>
      <p
        v-if="problem"
        role="alert"
        class="text-sm text-danger-600"
      >
        {{ problem }}
      </p>
    </div>
    <template #footer>
      <FmButton
        variant="ghost"
        @click="emit('update:modelValue', false)"
      >
        Cancel
      </FmButton>
      <FmButton
        :loading="submitting"
        data-testid="new-workflow-submit"
        @click="submit"
      >
        Create
      </FmButton>
    </template>
  </FmModal>
</template>
