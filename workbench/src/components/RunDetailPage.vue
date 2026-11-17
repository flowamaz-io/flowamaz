<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue';
import FmErrorState from './FmErrorState.vue';
import FmSpinner from './FmSpinner.vue';
import RunDetail from './RunDetail.vue';
import { describeError } from '../api';
import { useWorkbenchApi } from '../host';
import { isRunEnded } from '../runs';
import type { InstanceDetailResponse, TimelineResponse } from '../types';

// One run on the WorkbenchApi alone, refreshed while it is going. Cancel and retry are shown when canAct.
const props = withDefaults(defineProps<{ runId: string; canAct?: boolean; pollMs?: number }>(), { canAct: true, pollMs: 2000 });
const emit = defineEmits<{ retried: [newRunId: string]; back: [testRun: boolean] }>();

const api = useWorkbenchApi();
const run = ref<InstanceDetailResponse | null>(null);
const timeline = ref<TimelineResponse | null>(null);
const error = ref<{ message: string; code?: string; status?: number } | null>(null);
const actionError = ref<string | null>(null);
const acting = ref(false);
let timer: ReturnType<typeof setTimeout> | null = null;

function stop(): void {
  if (timer) clearTimeout(timer);
  timer = null;
}

async function load(): Promise<void> {
  stop();
  try {
    [run.value, timeline.value] = await Promise.all([api.getRun(props.runId), api.getRunTimeline(props.runId)]);
    error.value = null;
  } catch (e) {
    error.value = describeError(e);
  }
  if (run.value && !isRunEnded(run.value.status)) timer = setTimeout(() => void load(), props.pollMs);
}

async function act(action: () => Promise<void>): Promise<void> {
  acting.value = true;
  actionError.value = null;
  try {
    await action();
  } catch (e) {
    actionError.value = describeError(e).message;
  } finally {
    acting.value = false;
  }
}

const cancel = () => act(async () => { run.value = await api.cancelRun(props.runId); await load(); });
const retry = () => act(async () => { emit('retried', (await api.retryRun(props.runId)).instanceId); });

watch(() => props.runId, () => { run.value = null; timeline.value = null; void load(); });
onMounted(load);
onUnmounted(stop);
</script>

<template>
  <div>
    <FmErrorState
      v-if="error && !run"
      :title="error.status === 404 ? 'Run not found' : 'Couldn\'t load this run'"
      :description="error.message"
      :status-code="error.status"
      :error-code="error.code"
      :action-label="error.status === 404 ? 'Back to runs' : 'Retry'"
      @action="error.status === 404 ? emit('back', false) : load()"
    />
    <div
      v-else-if="!run"
      class="flex justify-center py-12"
    >
      <FmSpinner size="lg" />
    </div>
    <template v-else>
      <p
        v-if="actionError"
        role="alert"
        class="mb-4 rounded-lg border border-danger-200 bg-danger-50 px-4 py-3 text-sm text-danger-700"
      >
        {{ actionError }}
      </p>
      <RunDetail
        :run="run"
        :timeline="timeline"
        :can-act="canAct"
        :acting="acting"
        @cancel="cancel"
        @retry="retry"
      >
        <template #back>
          <button
            type="button"
            class="text-sm text-slate-400 hover:text-slate-600"
            @click="emit('back', run?.isTest === true)"
          >
            ← Runs
          </button>
        </template>
      </RunDetail>
    </template>
  </div>
</template>
