<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import FmModal from '@/components/common/FmModal.vue';
import FmButton from '@/components/common/FmButton.vue';
import { useWorkflowStore } from '@/stores/workflow.store';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import type { WorkflowDefinitionListItem } from '@/types';

const props = defineProps<{
  modelValue: boolean;
  workflows: WorkflowDefinitionListItem[];
  preselectedId?: string;
  forceTestRun?: boolean;
}>();

const emit = defineEmits<{ 'update:modelValue': [value: boolean]; triggered: [instanceId: string] }>();

const store = useWorkflowStore();
const toast = useToast();

const publishedWorkflows = computed(() => props.workflows.filter((w) => w.status === 'Published'));

const workflowId = ref(props.preselectedId ?? '');
const payload = ref('');
const idempotencyKey = ref('');
const payloadError = ref<string | null>(null);
const submitting = ref(false);

watch(
  () => props.modelValue,
  (open) => {
    if (open) {
      workflowId.value = props.preselectedId ?? publishedWorkflows.value[0]?.id ?? '';
      payload.value = '';
      idempotencyKey.value = '';
      payloadError.value = null;
    }
  },
);

const selectedWorkflow = computed(() =>
  props.workflows.find((w) => w.id === workflowId.value) ?? null,
);

const isTestRun = computed(() =>
  props.forceTestRun === true || selectedWorkflow.value?.status === 'Draft',
);

const canSubmit = computed(() => workflowId.value.length > 0 && !submitting.value);

function generateKey(): void {
  idempotencyKey.value = crypto.randomUUID();
}

function validPayload(): boolean {
  if (payload.value.trim().length === 0) return true;
  try {
    JSON.parse(payload.value);
    return true;
  } catch {
    payloadError.value = 'Payload must be valid JSON, or leave it empty.';
    return false;
  }
}

async function submit(): Promise<void> {
  payloadError.value = null;
  if (!validPayload()) return;
  submitting.value = true;
  try {
    const result = await store.triggerInstance({
      workflowDefinitionId: workflowId.value,
      payload: payload.value.trim().length > 0 ? payload.value : null,
      idempotencyKey: idempotencyKey.value.trim().length > 0 ? idempotencyKey.value : null,
      isTest: isTestRun.value,
    });
    const label = isTestRun.value ? 'Test run' : 'Run';
    toast.success(`${label} ${result.instanceId.slice(0, 8)} started`);
    emit('triggered', result.instanceId);
    emit('update:modelValue', false);
  } catch (err) {
    toast.error(toUserFacingError(err).message);
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <FmModal
    :model-value="modelValue"
    :title="isTestRun ? 'Start a test run' : 'Trigger a production run'"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div class="space-y-4">
      <div
        v-if="isTestRun"
        class="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800"
      >
        <p class="font-medium">
          Test run
        </p>
        <p class="mt-1">
          This run is isolated — no production instances are created. Human gate notifications are
          suppressed. Test results are deleted automatically after 24 hours.
        </p>
      </div>

      <div>
        <label
          for="trigger-workflow"
          class="mb-1 block text-sm font-medium text-slate-700"
        >Workflow</label>
        <select
          id="trigger-workflow"
          v-model="workflowId"
          class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
        >
          <option
            v-for="wf in workflows"
            :key="wf.id"
            :value="wf.id"
          >
            {{ wf.name }} ({{ wf.status }})
          </option>
        </select>
        <p
          v-if="selectedWorkflow?.status === 'Draft'"
          class="mt-1 text-xs text-amber-600"
        >
          Draft workflow — this will run as a test. Publish the workflow to trigger a production run.
        </p>
      </div>

      <div>
        <label
          for="trigger-payload"
          class="mb-1 block text-sm font-medium text-slate-700"
        >Payload (JSON, optional)</label>
        <textarea
          id="trigger-payload"
          v-model="payload"
          rows="5"
          placeholder="{ &quot;amount&quot;: 47500 }"
          class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 font-mono text-xs text-slate-900 focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
        />
        <p
          v-if="payloadError"
          class="mt-1 text-sm text-danger-600"
        >
          {{ payloadError }}
        </p>
      </div>

      <div>
        <label
          for="trigger-idem"
          class="mb-1 block text-sm font-medium text-slate-700"
        >Idempotency key (optional)</label>
        <div class="flex gap-2">
          <input
            id="trigger-idem"
            v-model="idempotencyKey"
            type="text"
            placeholder="Prevents duplicate triggers"
            class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
          >
          <FmButton
            variant="secondary"
            size="sm"
            @click="generateKey"
          >
            Generate
          </FmButton>
        </div>
      </div>
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
        :disabled="!canSubmit"
        :class="isTestRun ? 'border border-amber-500 bg-white text-amber-700 hover:bg-amber-50' : ''"
        @click="submit"
      >
        {{ isTestRun ? 'Start test run' : 'Trigger run' }}
      </FmButton>
    </template>
  </FmModal>
</template>
