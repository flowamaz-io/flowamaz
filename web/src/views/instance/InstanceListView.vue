<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue';
import { Activity } from 'lucide-vue-next';
import { storeToRefs } from 'pinia';
import FmBadge from '@/components/common/FmBadge.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import { useWorkflowStore } from '@/stores/workflow.store';
import { useWorkspace } from '@/composables/useWorkspace';
import { useAuthStore } from '@/stores/auth.store';
import { useInstanceWebSocket, type InstanceWebSocket } from '@/composables/useInstanceWebSocket';
import { fromNow } from '@/utils/date.util';
import { formatDuration, instanceStatusVariant } from '@/utils/workflow.util';
import type { InstanceListFilters } from '@/services/instance.service';
import type { InstanceStatus } from '@/types';

type RunTab = 'production' | 'test';

const TERMINAL: InstanceStatus[] = ['Completed', 'Failed', 'Cancelled'];
const MAX_LIVE_SOCKETS = 20;

const store = useWorkflowStore();
const auth = useAuthStore();
const { instances, loading, error } = storeToRefs(store);
const { currentWorkspaceId, loadWorkspaces } = useWorkspace();

const activeTab = ref<RunTab>('production');
const statusFilter = ref<InstanceStatus | ''>('');
const from = ref('');
const to = ref('');

let sockets: InstanceWebSocket[] = [];

const liveCount = computed(() => sockets.filter((s) => s.connectionState.value === 'live').length);
const anyReconnecting = computed(() => sockets.some((s) => s.connectionState.value === 'reconnecting'));

function hoursUntilExpiry(expiresAt: string | null): string {
  if (!expiresAt) return '';
  const diff = new Date(expiresAt).getTime() - Date.now();
  if (diff <= 0) return 'Expired';
  const h = Math.floor(diff / 3_600_000);
  const m = Math.floor((diff % 3_600_000) / 60_000);
  return h > 0 ? `Expires in ${h}h ${m}m` : `Expires in ${m}m`;
}

function disconnectAll(): void {
  for (const socket of sockets) socket.disconnect();
  sockets = [];
}

function connectLive(): void {
  disconnectAll();
  if (!currentWorkspaceId.value) return;
  const wsId = currentWorkspaceId.value;
  const live = instances.value.filter((i) => !TERMINAL.includes(i.status)).slice(0, MAX_LIVE_SOCKETS);
  for (const inst of live) {
    const socket = useInstanceWebSocket(
      wsId,
      inst.id,
      () => auth.accessToken,
      (frame) => store.applyStatusUpdate(inst.id, frame.status as InstanceStatus, frame.nodeId),
    );
    socket.connect();
    sockets.push(socket);
  }
}

async function reload(): Promise<void> {
  if (!currentWorkspaceId.value) await loadWorkspaces();
  const filters: InstanceListFilters = { includeTest: activeTab.value === 'test' };
  if (statusFilter.value) filters.status = statusFilter.value;
  if (from.value) filters.from = from.value;
  if (to.value) filters.to = to.value;
  await store.loadInstances(filters);
  connectLive();
}

function switchTab(tab: RunTab): void {
  activeTab.value = tab;
  reload();
}

onMounted(reload);
onUnmounted(disconnectAll);
</script>

<template>
  <div class="space-y-6 px-8 py-6">
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-xl font-semibold text-slate-900">
          Instances
        </h1>
        <p class="text-sm text-slate-500">
          Monitor every workflow run.
        </p>
      </div>
      <div class="flex items-center gap-2 text-sm">
        <Activity
          :class="['h-4 w-4', liveCount > 0 ? 'text-teal-500' : anyReconnecting ? 'text-amber-500' : 'text-slate-300']"
        />
        <span class="text-slate-500">
          {{ liveCount > 0 ? `Live · ${liveCount}` : anyReconnecting ? 'Reconnecting…' : 'Idle' }}
        </span>
      </div>
    </div>

    <div class="flex gap-1 border-b border-slate-200">
      <button
        type="button"
        :class="[
          '-mb-px border-b-2 px-4 py-2 text-sm font-medium',
          activeTab === 'production'
            ? 'border-primary-600 text-primary-700'
            : 'border-transparent text-slate-500 hover:text-slate-700',
        ]"
        @click="switchTab('production')"
      >
        Production
      </button>
      <button
        type="button"
        :class="[
          '-mb-px border-b-2 px-4 py-2 text-sm font-medium',
          activeTab === 'test'
            ? 'border-amber-500 text-amber-700'
            : 'border-transparent text-slate-500 hover:text-slate-700',
        ]"
        @click="switchTab('test')"
      >
        Test
      </button>
    </div>

    <div class="flex flex-wrap items-center gap-3">
      <select
        v-model="statusFilter"
        class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
        @change="reload"
      >
        <option value="">
          All statuses
        </option>
        <option
          v-for="s in ['Pending','Running','Waiting','Completed','Failed','Cancelled','Compensating']"
          :key="s"
          :value="s"
        >
          {{ s }}
        </option>
      </select>
      <input
        v-model="from"
        type="date"
        class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm"
        @change="reload"
      >
      <input
        v-model="to"
        type="date"
        class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm"
        @change="reload"
      >
    </div>

    <div
      v-if="loading"
      class="flex justify-center py-12"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="error"
      title="Couldn't load instances"
      :description="error"
      action-label="Try again"
      @action="reload"
    />

    <FmEmptyState
      v-else-if="instances.length === 0"
      :icon="Activity"
      :title="activeTab === 'test' ? 'No test runs yet' : 'No runs yet'"
      :description="activeTab === 'test'
        ? 'Click Test run on any workflow to try it without creating a production instance.'
        : 'Trigger a published workflow to see runs appear here in real time.'"
      cta-label="Go to Workflows"
      @cta="$router.push('/workflows')"
    />

    <div
      v-else
      class="overflow-hidden rounded-xl border border-slate-200 bg-white"
    >
      <table class="min-w-full divide-y divide-slate-200 text-sm">
        <thead class="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-500">
          <tr>
            <th class="px-4 py-3">
              Instance
            </th>
            <th class="px-4 py-3">
              Status
            </th>
            <th class="px-4 py-3">
              Trigger
            </th>
            <th class="px-4 py-3">
              Started
            </th>
            <th class="px-4 py-3">
              Duration
            </th>
            <th
              v-if="activeTab === 'test'"
              class="px-4 py-3"
            >
              Expiry
            </th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
          <tr
            v-for="inst in instances"
            :key="inst.id"
            class="cursor-pointer hover:bg-slate-50"
            @click="$router.push(`/instances/${inst.id}`)"
          >
            <td class="px-4 py-3 font-mono text-xs text-primary-600">
              {{ inst.id.slice(0, 8) }}
              <span
                v-if="inst.isTest"
                class="ml-2 inline-flex items-center rounded px-1.5 py-0.5 text-[10px] font-semibold bg-amber-100 text-amber-700"
              >
                TEST
              </span>
            </td>
            <td class="px-4 py-3">
              <FmBadge :variant="instanceStatusVariant(inst.status)">
                {{ inst.status }}
              </FmBadge>
            </td>
            <td class="px-4 py-3 text-slate-500">
              {{ inst.triggerType }}
            </td>
            <td class="px-4 py-3 text-slate-500">
              {{ fromNow(inst.startedAt ?? inst.createdAt) }}
            </td>
            <td class="px-4 py-3 tabular-nums text-slate-500">
              {{ formatDuration(inst.startedAt, inst.completedAt) }}
            </td>
            <td
              v-if="activeTab === 'test'"
              class="px-4 py-3 text-xs text-amber-600"
            >
              {{ hoursUntilExpiry(inst.testExpiresAt) }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
