<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { CheckCircle, XCircle, Clock } from 'lucide-vue-next';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import { gateService } from '@/services/gate.service';
import { useWorkspace } from '@/composables/useWorkspace';
import { formatDate, fromNow } from '@/utils/date.util';
import type { GateResponse } from '@/types';

type Tab = 'pending' | 'all';

const { currentWorkspaceId, loadWorkspaces } = useWorkspace();

const allGates = ref<GateResponse[]>([]);
const loading = ref(false);
const error = ref<string | null>(null);
const activeTab = ref<Tab>('pending');

// Per-row note inputs and submitting state.
const notes = ref<Record<string, string>>({});
const submitting = ref<Record<string, boolean>>({});

const pendingCount = computed(() =>
  allGates.value.filter((g) => g.decision === 'Pending').length,
);

const displayedGates = computed<GateResponse[]>(() =>
  activeTab.value === 'pending'
    ? allGates.value.filter((g) => g.decision === 'Pending')
    : allGates.value,
);

async function load(): Promise<void> {
  if (!currentWorkspaceId.value) await loadWorkspaces();
  if (!currentWorkspaceId.value) return;

  loading.value = true;
  error.value = null;

  try {
    allGates.value = await gateService.getPendingGates(currentWorkspaceId.value);
  } catch (err: unknown) {
    error.value =
      err instanceof Error
        ? err.message
        : 'Failed to load pending gates. Check your network connection and try again.';
  } finally {
    loading.value = false;
  }
}

async function decide(
  gate: GateResponse,
  decision: 'approved' | 'rejected',
): Promise<void> {
  if (!currentWorkspaceId.value) return;

  const key = `${gate.instanceId}-${gate.nodeId}`;
  submitting.value[key] = true;

  try {
    const updated = await gateService.decideGate(
      currentWorkspaceId.value,
      gate.instanceId,
      gate.nodeId,
      decision,
      notes.value[key] || undefined,
    );

    const idx = allGates.value.findIndex(
      (g) => g.instanceId === gate.instanceId && g.nodeId === gate.nodeId,
    );
    if (idx !== -1) allGates.value[idx] = updated;

    delete notes.value[key];
  } catch (err: unknown) {
    error.value =
      err instanceof Error
        ? err.message
        : `Failed to record decision. Please reload and try again.`;
  } finally {
    submitting.value[key] = false;
  }
}

function rowKey(gate: GateResponse): string {
  return `${gate.instanceId}-${gate.nodeId}`;
}

function decisionVariant(decision: string): 'primary' | 'accent' | 'amber' | 'danger' | 'slate' | 'blue' {
  if (decision === 'Approved') return 'accent';
  if (decision === 'Rejected') return 'danger';
  if (decision === 'Expired' || decision === 'Escalated') return 'amber';
  return 'slate';
}

onMounted(load);
</script>

<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-xl font-semibold text-slate-900">
          Approval Gates
        </h1>
        <p class="text-sm text-slate-500">
          Review and decide pending workflow approvals.
        </p>
      </div>
      <div
        v-if="pendingCount > 0"
        class="flex items-center gap-2"
      >
        <span class="inline-flex h-6 w-6 items-center justify-center rounded-full bg-amber-500 text-xs font-bold text-white">
          {{ pendingCount }}
        </span>
        <span class="text-sm font-medium text-amber-700">pending</span>
      </div>
    </div>

    <!-- Tabs -->
    <div class="flex gap-1 rounded-lg border border-slate-200 bg-slate-50 p-1 w-fit">
      <button
        :class="[
          'rounded-md px-4 py-1.5 text-sm font-medium transition-colors',
          activeTab === 'pending'
            ? 'bg-white text-slate-900 shadow-sm'
            : 'text-slate-500 hover:text-slate-700',
        ]"
        @click="activeTab = 'pending'"
      >
        Pending
        <span
          v-if="pendingCount > 0"
          class="ml-1.5 rounded-full bg-amber-100 px-1.5 py-0.5 text-xs font-semibold text-amber-700"
        >
          {{ pendingCount }}
        </span>
      </button>
      <button
        :class="[
          'rounded-md px-4 py-1.5 text-sm font-medium transition-colors',
          activeTab === 'all'
            ? 'bg-white text-slate-900 shadow-sm'
            : 'text-slate-500 hover:text-slate-700',
        ]"
        @click="activeTab = 'all'"
      >
        All
      </button>
    </div>

    <!-- Loading -->
    <div
      v-if="loading"
      class="flex justify-center py-16"
    >
      <FmSpinner size="lg" />
    </div>

    <!-- Error -->
    <FmErrorState
      v-else-if="error"
      title="Couldn't load gates"
      :description="error"
      action-label="Try again"
      @action="load"
    />

    <!-- Empty -->
    <FmEmptyState
      v-else-if="displayedGates.length === 0"
      :icon="CheckCircle"
      :title="activeTab === 'pending' ? 'No pending approvals' : 'No gates yet'"
      :description="
        activeTab === 'pending'
          ? 'All caught up — no workflows are waiting for your approval right now.'
          : 'Gates will appear here once workflows reach a human-approval step.'
      "
    />

    <!-- Table -->
    <div
      v-else
      class="overflow-hidden rounded-xl border border-slate-200 bg-white"
    >
      <table class="min-w-full divide-y divide-slate-200 text-sm">
        <thead class="bg-slate-50">
          <tr>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
              Gate
            </th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
              Assignee
            </th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
              Channel
            </th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
              Submitted
            </th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
              Expires
            </th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
              Status
            </th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
              Note
            </th>
            <th class="px-4 py-3" />
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
          <tr
            v-for="gate in displayedGates"
            :key="gate.id"
            class="hover:bg-slate-50 transition-colors"
          >
            <!-- Gate node info -->
            <td class="px-4 py-3">
              <div class="font-medium text-slate-800">
                {{ gate.nodeId }}
              </div>
              <div class="text-xs text-slate-400 font-mono">
                {{ gate.instanceId.slice(0, 8) }}…
              </div>
            </td>

            <!-- Assignee -->
            <td class="px-4 py-3 text-slate-600">
              {{ gate.assignedToEmail ?? '—' }}
            </td>

            <!-- Channel -->
            <td class="px-4 py-3 text-slate-600 capitalize">
              {{ gate.deliveryChannel.toLowerCase() }}
            </td>

            <!-- Submitted -->
            <td class="px-4 py-3 text-slate-500">
              <span :title="formatDate(gate.createdAt)">
                {{ fromNow(gate.createdAt) }}
              </span>
            </td>

            <!-- Expires -->
            <td class="px-4 py-3 text-slate-500">
              <span
                v-if="gate.expiresAt"
                :title="formatDate(gate.expiresAt)"
              >
                {{ fromNow(gate.expiresAt) }}
              </span>
              <span v-else class="text-slate-300">—</span>
            </td>

            <!-- Status badge -->
            <td class="px-4 py-3">
              <FmBadge :variant="decisionVariant(gate.decision)">
                {{ gate.decision }}
              </FmBadge>
            </td>

            <!-- Note input (pending only) -->
            <td class="px-4 py-3">
              <input
                v-if="gate.decision === 'Pending'"
                v-model="notes[rowKey(gate)]"
                type="text"
                placeholder="Optional note…"
                class="w-40 rounded-md border border-slate-200 px-2 py-1 text-xs text-slate-700 placeholder:text-slate-400 focus:border-primary-400 focus:outline-none focus:ring-1 focus:ring-primary-200"
              >
              <span
                v-else-if="gate.decisionNote"
                class="text-xs text-slate-500 italic"
              >
                {{ gate.decisionNote }}
              </span>
              <span v-else class="text-slate-300">—</span>
            </td>

            <!-- Actions -->
            <td class="px-4 py-3">
              <div
                v-if="gate.decision === 'Pending'"
                class="flex items-center gap-2"
              >
                <button
                  :disabled="submitting[rowKey(gate)]"
                  class="inline-flex items-center gap-1 rounded-md bg-emerald-50 px-3 py-1.5 text-xs font-semibold text-emerald-700 hover:bg-emerald-100 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                  @click="decide(gate, 'approved')"
                >
                  <CheckCircle class="h-3.5 w-3.5" />
                  Approve
                </button>
                <button
                  :disabled="submitting[rowKey(gate)]"
                  class="inline-flex items-center gap-1 rounded-md bg-red-50 px-3 py-1.5 text-xs font-semibold text-red-700 hover:bg-red-100 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                  @click="decide(gate, 'rejected')"
                >
                  <XCircle class="h-3.5 w-3.5" />
                  Reject
                </button>
              </div>
              <div
                v-else
                class="flex items-center gap-1 text-xs text-slate-400"
              >
                <Clock class="h-3.5 w-3.5" />
                {{ gate.decidedAt ? formatDate(gate.decidedAt) : gate.decision }}
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
