<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { storeToRefs } from 'pinia';
import FmBadge from '@/components/common/FmBadge.vue';
import FmButton from '@/components/common/FmButton.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import FmRunTimeline from '@/components/workflow/FmRunTimeline.vue';
import FmInterpreterPanel from '@/components/workflow/FmInterpreterPanel.vue';
import StepDebugger from '@/components/instance/StepDebugger.vue';
import ReplayModal from '@/components/instance/ReplayModal.vue';
import VariableInspector from '@/components/instance/VariableInspector.vue';
import { useWorkflowStore } from '@/stores/workflow.store';
import { useWorkspace } from '@/composables/useWorkspace';
import { useAuthStore } from '@/stores/auth.store';
import { useToast } from '@/composables/useToast';
import { useInstanceWebSocket } from '@/composables/useInstanceWebSocket';
import { toUserFacingError } from '@/utils/error.util';
import { fromNow } from '@/utils/date.util';
import { formatDuration, instanceStatusVariant } from '@/utils/workflow.util';
import type { InstanceStatus } from '@/types';

type Tab = 'timeline' | 'nodes' | 'variables' | 'events' | 'narrative';

const route = useRoute();
const router = useRouter();
const store = useWorkflowStore();
const auth = useAuthStore();
const toast = useToast();
const { currentInstance, timeline, loading, error } = storeToRefs(store);
const { currentWorkspaceId, loadWorkspaces } = useWorkspace();

const id = computed(() => String(route.params.id));
const activeTab = ref<Tab>('timeline');
const acting = ref(false);
const replayOpen = ref(false);

const canCancel = computed(() => {
  const s = currentInstance.value?.status;
  return s === 'Pending' || s === 'Running' || s === 'Waiting' || s === 'Compensating';
});
const canRetry = computed(() => currentInstance.value?.status === 'Failed');
const isTerminal = computed(() => {
  const s = currentInstance.value?.status;
  return s === 'Completed' || s === 'Failed' || s === 'Cancelled';
});

// The trigger payload is recorded on the first InstanceStarted event — used to pre-fill the
// replay editor. The server re-reads the authoritative payload when replaying.
const originalPayload = computed<string | null>(() => {
  const started = currentInstance.value?.events.find((e) => e.eventType === 'InstanceStarted');
  return started?.payload ?? null;
});

async function refresh(): Promise<void> {
  await Promise.all([store.loadInstanceDetail(id.value), store.loadTimeline(id.value)]);
}

async function cancel(): Promise<void> {
  acting.value = true;
  try {
    await store.cancelInstance(id.value);
    toast.success('Run cancelled.');
  } catch (err) {
    toast.error(toUserFacingError(err).message);
  } finally {
    acting.value = false;
  }
}

async function retry(): Promise<void> {
  acting.value = true;
  try {
    const result = await store.retryInstance(id.value);
    toast.success('Retry started.');
    await router.push(`/instances/${result.instanceId}`);
  } catch (err) {
    toast.error(toUserFacingError(err).message);
  } finally {
    acting.value = false;
  }
}

const ws = useInstanceWebSocket(
  currentWorkspaceId.value ?? '',
  id.value,
  () => auth.accessToken,
  (frame) => {
    store.applyStatusUpdate(id.value, frame.status as InstanceStatus, frame.nodeId);
    void store.loadTimeline(id.value);
  },
);

onMounted(async () => {
  if (!currentWorkspaceId.value) await loadWorkspaces();
  await refresh();
  ws.connect();
});
</script>

<template>
  <div class="space-y-6">
    <div
      v-if="loading && !currentInstance"
      class="flex justify-center py-12"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="error && !currentInstance"
      title="Couldn't load this run"
      :description="error"
      action-label="Retry"
      @action="refresh"
    />

    <template v-else-if="currentInstance">
      <div class="flex items-start justify-between">
        <div>
          <RouterLink
            to="/instances"
            class="text-sm text-slate-400 hover:text-slate-600"
          >
            ← Instances
          </RouterLink>
          <h1 class="mt-1 font-mono text-lg font-semibold text-slate-900">
            {{ currentInstance.id.slice(0, 8) }}
          </h1>
          <div class="mt-2 flex items-center gap-3 text-sm">
            <FmBadge :variant="instanceStatusVariant(currentInstance.status)">
              {{ currentInstance.status }}
            </FmBadge>
            <span class="text-slate-500">{{ formatDuration(currentInstance.startedAt, currentInstance.completedAt) }}</span>
            <span
              v-if="ws.connectionState.value === 'live'"
              class="text-xs text-teal-600"
            >● Live</span>
            <span
              v-else-if="ws.connectionState.value === 'reconnecting'"
              class="text-xs text-amber-600"
            >Reconnecting…</span>
          </div>
        </div>
        <div class="flex gap-2">
          <FmButton
            v-if="canCancel"
            variant="secondary"
            :loading="acting"
            @click="cancel"
          >
            Cancel
          </FmButton>
          <FmButton
            v-if="canRetry"
            :loading="acting"
            @click="retry"
          >
            Retry
          </FmButton>
          <FmButton
            v-if="isTerminal"
            variant="secondary"
            @click="replayOpen = true"
          >
            Replay
          </FmButton>
        </div>
      </div>

      <div class="flex gap-1 border-b border-slate-200">
        <button
          v-for="tab in (['timeline', 'nodes', 'variables', 'events', 'narrative'] as Tab[])"
          :key="tab"
          type="button"
          :class="[
            '-mb-px border-b-2 px-4 py-2 text-sm font-medium capitalize',
            activeTab === tab ? 'border-primary-600 text-primary-700' : 'border-transparent text-slate-500 hover:text-slate-700',
          ]"
          @click="activeTab = tab"
        >
          {{ tab }}
        </button>
      </div>

      <div
        v-if="activeTab === 'timeline'"
        class="space-y-4"
      >
        <div class="rounded-xl border border-slate-200 bg-white p-5">
          <FmRunTimeline
            v-if="timeline && timeline.nodes.length > 0"
            :timeline="timeline"
          />
          <p
            v-else
            class="text-sm text-slate-400"
          >
            No node activity yet. Trigger or replay this workflow to populate the timeline.
          </p>
        </div>
        <StepDebugger
          :node-states="currentInstance.nodeStates"
          :events="currentInstance.events"
        />
      </div>

      <div
        v-else-if="activeTab === 'nodes'"
        class="overflow-hidden rounded-xl border border-slate-200 bg-white"
      >
        <table class="min-w-full divide-y divide-slate-200 text-sm">
          <thead class="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-500">
            <tr>
              <th class="px-4 py-3">
                Node
              </th>
              <th class="px-4 py-3">
                Status
              </th>
              <th class="px-4 py-3">
                Retries
              </th>
              <th class="px-4 py-3">
                Error
              </th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-100">
            <tr
              v-for="node in currentInstance.nodeStates"
              :key="node.nodeId"
            >
              <td class="px-4 py-3">
                <span class="font-medium text-slate-800">{{ node.nodeId }}</span>
                <span class="ml-2 text-xs text-slate-400">{{ node.nodeType }}</span>
              </td>
              <td class="px-4 py-3 text-slate-600">
                {{ node.status }}
              </td>
              <td class="px-4 py-3 tabular-nums text-slate-600">
                {{ node.retryCount }}
              </td>
              <td class="px-4 py-3 text-xs text-danger-600">
                {{ node.errorMessage ?? '—' }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-else-if="activeTab === 'variables'">
        <VariableInspector
          :workspace-id="currentWorkspaceId ?? ''"
          :instance-id="currentInstance.id"
          :status="currentInstance.status"
        />
      </div>

      <div
        v-else-if="activeTab === 'events'"
        class="space-y-2"
      >
        <div
          v-for="event in currentInstance.events"
          :key="event.sequenceNumber"
          class="rounded-lg border border-slate-200 bg-white px-4 py-2 text-sm"
        >
          <div class="flex items-center justify-between">
            <span class="font-medium text-slate-800">{{ event.eventType }}</span>
            <span class="text-xs text-slate-400">{{ fromNow(event.occurredAt) }}</span>
          </div>
          <p class="text-xs text-slate-500">
            {{ event.nodeId ?? '—' }} · {{ event.payload }}
          </p>
        </div>
      </div>

      <div v-else>
        <FmInterpreterPanel
          :workspace-id="currentWorkspaceId ?? ''"
          :instance-id="currentInstance.id"
          :instance-status="currentInstance.status"
        />
      </div>

      <ReplayModal
        v-model="replayOpen"
        :workspace-id="currentWorkspaceId ?? ''"
        :instance-id="currentInstance.id"
        :original-payload="originalPayload"
      />
    </template>
  </div>
</template>
