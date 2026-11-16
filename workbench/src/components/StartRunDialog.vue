<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import FmButton from './FmButton.vue';
import FmModal from './FmModal.vue';
import { describeError } from '../api';
import { useWorkbenchApi } from '../host';
import type { WorkflowDefinitionListItem } from '../types';

// Start a run of one workflow: an optional JSON payload and idempotency key. A draft runs as a test run.
const props = defineProps<{ modelValue: boolean; workflow: WorkflowDefinitionListItem | null }>();
const emit = defineEmits<{ 'update:modelValue': [open: boolean]; started: [runId: string, isTest: boolean] }>();

const api = useWorkbenchApi();
const payload = ref('');
const idempotencyKey = ref('');
const problem = ref<string | null>(null);
const submitting = ref(false);
const isTest = computed(() => props.workflow?.status === 'Draft');

watch(() => props.modelValue, (open) => {
  if (!open) return;
  payload.value = '';
  idempotencyKey.value = '';
  problem.value = null;
});

async function submit(): Promise<void> {
  if (!props.workflow) return;
  problem.value = null;
  const text = payload.value.trim();
  if (text) {
    try { JSON.parse(text); } catch {
      problem.value = 'The payload must be valid JSON (for example { "amount": 100 }), or left empty.';
      return;
    }
  }
  submitting.value = true;
  try {
    const run = await api.startRun(props.workflow.id, {
      payload: text || null, isTest: isTest.value, idempotencyKey: idempotencyKey.value.trim() || null,
    });
    emit('started', run.instanceId, isTest.value);
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
    :title="isTest ? 'Start a test run' : 'Start a run'"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div class="space-y-4">
      <p
        v-if="isTest"
        class="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800"
      >
        This workflow is a draft, so this is a test run: it's kept apart from production runs and deleted after 24 hours.
        Publish the workflow to start production runs.
      </p>
      <p class="text-sm text-slate-600">
        {{ workflow?.name }}
      </p>
      <div>
        <label
          for="start-run-payload"
          class="mb-1 block text-sm font-medium text-slate-700"
        >Payload (JSON, optional)</label>
        <textarea
          id="start-run-payload"
          v-model="payload"
          rows="5"
          placeholder="{ &quot;amount&quot;: 47500 }"
          class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 font-mono text-xs text-slate-900 focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
        />
      </div>
      <div>
        <label
          for="start-run-key"
          class="mb-1 block text-sm font-medium text-slate-700"
        >Idempotency key (optional)</label>
        <input
          id="start-run-key"
          v-model="idempotencyKey"
          type="text"
          placeholder="Starting twice with the same key gives the same run"
          class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
        >
      </div>
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
        :variant="isTest ? 'warning-outline' : 'primary'"
        data-testid="start-run-submit"
        @click="submit"
      >
        {{ isTest ? 'Start test run' : 'Start run' }}
      </FmButton>
    </template>
  </FmModal>
</template>
