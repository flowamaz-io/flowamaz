<script setup lang="ts">
import { ref, watch } from 'vue';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmModal from '@/components/common/FmModal.vue';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { workflowService } from '@/services/workflow.service';
import { webhookService, type CreatedWebhook } from '@/services/webhook.service';
import type { WorkflowDefinitionListItem } from '@/types';

const props = defineProps<{ modelValue: boolean; workspaceId: string }>();
const emit = defineEmits<{ 'update:modelValue': [value: boolean]; created: [webhook: CreatedWebhook] }>();

const toast = useToast();

const publishedWorkflows = ref<WorkflowDefinitionListItem[]>([]);
const loadingWorkflows = ref(false);
const workflowId = ref('');
const description = ref('');
const creating = ref(false);

async function loadPublishedWorkflows(): Promise<void> {
  loadingWorkflows.value = true;
  try {
    const page = await workflowService.list(props.workspaceId, 1, 100);
    publishedWorkflows.value = page.data.filter((w) => w.status === 'Published');
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    loadingWorkflows.value = false;
  }
}

watch(
  () => props.modelValue,
  (open) => {
    if (open) {
      workflowId.value = '';
      description.value = '';
      void loadPublishedWorkflows();
    }
  },
);

async function submit(): Promise<void> {
  if (!workflowId.value) {
    toast.error('Select a published workflow for this webhook to trigger.');
    return;
  }
  creating.value = true;
  try {
    const created = await webhookService.create(props.workspaceId, {
      workflowDefinitionId: workflowId.value,
      description: description.value.trim() || null,
    });
    emit('created', created);
    emit('update:modelValue', false);
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    creating.value = false;
  }
}
</script>

<template>
  <FmModal
    :model-value="modelValue"
    title="Create webhook"
    size="md"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div class="space-y-4">
      <div>
        <label
          for="webhook-workflow"
          class="mb-1 block text-sm font-medium text-slate-700"
        >Workflow</label>
        <select
          id="webhook-workflow"
          v-model="workflowId"
          class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
        >
          <option
            value=""
            disabled
          >
            {{ loadingWorkflows ? 'Loading workflows…' : 'Select a published workflow' }}
          </option>
          <option
            v-for="wf in publishedWorkflows"
            :key="wf.id"
            :value="wf.id"
          >
            {{ wf.name }}
          </option>
        </select>
        <p
          v-if="!loadingWorkflows && publishedWorkflows.length === 0"
          class="mt-1 text-xs text-slate-500"
        >
          No published workflows yet. Publish a workflow first, then create a webhook to trigger it.
        </p>
      </div>
      <FmInput
        v-model="description"
        label="Description"
        placeholder="e.g. Stripe payment events"
        help="An optional note to help you recognise this webhook later."
      />
    </div>
    <template #footer>
      <FmButton
        variant="secondary"
        @click="emit('update:modelValue', false)"
      >
        Cancel
      </FmButton>
      <FmButton
        :loading="creating"
        @click="submit"
      >
        Create webhook
      </FmButton>
    </template>
  </FmModal>
</template>
