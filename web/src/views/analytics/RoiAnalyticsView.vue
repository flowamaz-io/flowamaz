<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { BarChart2, Download } from 'lucide-vue-next';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import RoiConfigModal from '@/components/analytics/RoiConfigModal.vue';
import { analyticsService } from '@/services/analytics.service';
import { useWorkspaceStore } from '@/stores/workspace.store';
import { toUserFacingError } from '@/utils/error.util';
import type { WorkspaceRoiSummary, WorkflowRoiDetail } from '@/types';

type RangeKey = 'month' | 'quarter' | 'year';

const workspaceStore = useWorkspaceStore();
const workspaceId = computed(() => workspaceStore.currentWorkspaceId ?? '');

const range = ref<RangeKey>('month');
const summary = ref<WorkspaceRoiSummary | null>(null);
const loading = ref(false);
const error = ref<string | null>(null);

const configOpen = ref(false);
const configWorkflowId = ref('');
const configWorkflowName = ref('');

const ranges: Array<{ key: RangeKey; label: string }> = [
  { key: 'month', label: 'This month' },
  { key: 'quarter', label: 'Last 3 months' },
  { key: 'year', label: 'Last 12 months' },
];

function rangeBounds(key: RangeKey): { from: string; to: string } {
  const now = new Date();
  const to = now.toISOString();
  let from: Date;
  if (key === 'month') from = new Date(now.getFullYear(), now.getMonth(), 1);
  else if (key === 'quarter') from = new Date(now.getFullYear(), now.getMonth() - 3, 1);
  else from = new Date(now.getFullYear() - 1, now.getMonth(), 1);
  return { from: from.toISOString(), to };
}

const currency = computed(() => summary.value?.byWorkflow.find((w) => w.configured)?.currency ?? 'MYR');

function money(value: number): string {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency: currency.value, maximumFractionDigits: 2 }).format(value);
}

function timeSaved(minutes: number): string {
  if (minutes < 60) return `${minutes}m`;
  const hours = Math.floor(minutes / 60);
  const mins = minutes % 60;
  return mins === 0 ? `${hours}h` : `${hours}h ${mins}m`;
}

// Horizontal bar chart: cost avoided by workflow (top contributors).
const chartRows = computed(() => {
  const rows = (summary.value?.byWorkflow ?? []).filter((w) => w.costAvoided > 0);
  const max = Math.max(1, ...rows.map((w) => w.costAvoided));
  return rows.slice(0, 8).map((w) => ({ ...w, widthPct: Math.round((w.costAvoided / max) * 100) }));
});

async function load(): Promise<void> {
  if (!workspaceId.value) return;
  loading.value = true;
  error.value = null;
  try {
    const { from, to } = rangeBounds(range.value);
    summary.value = await analyticsService.roi(workspaceId.value, from, to);
  } catch (err) {
    error.value = toUserFacingError(err).message;
  } finally {
    loading.value = false;
  }
}

function selectRange(key: RangeKey): void {
  range.value = key;
  void load();
}

function openConfig(row: WorkflowRoiDetail): void {
  configWorkflowId.value = row.workflowDefinitionId;
  configWorkflowName.value = row.workflowName;
  configOpen.value = true;
}

function exportCsv(): void {
  const rows = summary.value?.byWorkflow ?? [];
  const header = ['Workflow', 'Runs', 'Successful', 'Time saved (min)', 'Cost avoided', 'ROI %', 'Currency'];
  const lines = rows.map((w) =>
    [w.workflowName, w.runs, w.successfulRuns, w.timeSavedMinutes, w.costAvoided, w.roiPercentage, w.currency]
      .map((v) => `"${String(v).replace(/"/g, '""')}"`)
      .join(','),
  );
  const csv = [header.join(','), ...lines].join('\n');
  const blob = new Blob([csv], { type: 'text/csv' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `roi-${range.value}.csv`;
  a.click();
  URL.revokeObjectURL(url);
}

onMounted(load);
</script>

<template>
  <div class="space-y-6 px-8 py-6">
    <div class="flex items-center justify-between">
      <div>
        <h1 class="flex items-center gap-2 text-xl font-semibold text-slate-900">
          <BarChart2 class="h-5 w-5 text-primary-600" />
          ROI Analytics
        </h1>
        <p class="text-sm text-slate-500">
          Time saved and cost avoided by your automations — based on your configured baselines and actual runs.
        </p>
      </div>
      <button
        type="button"
        class="flex items-center gap-2 rounded-lg border border-slate-300 px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50"
        @click="exportCsv"
      >
        <Download class="h-4 w-4" />
        Export CSV
      </button>
    </div>

    <div class="flex gap-2">
      <button
        v-for="r in ranges"
        :key="r.key"
        type="button"
        :class="[
          'rounded-lg px-3 py-1.5 text-sm font-medium',
          range === r.key ? 'bg-primary-600 text-white' : 'border border-slate-300 text-slate-600 hover:bg-slate-50',
        ]"
        @click="selectRange(r.key)"
      >
        {{ r.label }}
      </button>
    </div>

    <div
      v-if="loading"
      class="flex justify-center py-12"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="error"
      title="Couldn't load ROI analytics"
      :description="error"
      action-label="Retry"
      @action="load"
    />

    <template v-else-if="summary">
      <div class="grid grid-cols-2 gap-4 md:grid-cols-4">
        <div class="rounded-xl border border-slate-200 bg-white p-5">
          <p class="text-sm text-slate-400">
            Time saved
          </p>
          <p class="mt-1 text-2xl font-semibold text-slate-900">
            {{ timeSaved(summary.totalTimeSavedMinutes) }}
          </p>
        </div>
        <div class="rounded-xl border border-slate-200 bg-white p-5">
          <p class="text-sm text-slate-400">
            Cost avoided
          </p>
          <p class="mt-1 text-2xl font-semibold text-emerald-600">
            {{ money(summary.totalCostAvoided) }}
          </p>
        </div>
        <div class="rounded-xl border border-slate-200 bg-white p-5">
          <p class="text-sm text-slate-400">
            ROI
          </p>
          <p class="mt-1 text-2xl font-semibold text-slate-900">
            {{ summary.roiPercentage }}%
          </p>
        </div>
        <div class="rounded-xl border border-slate-200 bg-white p-5">
          <p class="text-sm text-slate-400">
            Successful runs
          </p>
          <p class="mt-1 text-2xl font-semibold text-slate-900">
            {{ summary.successfulRuns }}
          </p>
        </div>
      </div>

      <div
        v-if="chartRows.length > 0"
        class="rounded-xl border border-slate-200 bg-white p-5"
      >
        <h2 class="mb-4 text-sm font-semibold text-slate-700">
          Cost avoided by workflow
        </h2>
        <div class="space-y-3">
          <div
            v-for="row in chartRows"
            :key="row.workflowDefinitionId"
            class="flex items-center gap-3"
          >
            <span class="w-40 shrink-0 truncate text-sm text-slate-600">{{ row.workflowName }}</span>
            <div class="h-6 flex-1 overflow-hidden rounded bg-slate-100">
              <div
                class="flex h-full items-center justify-end rounded bg-emerald-500 px-2 text-xs font-medium text-white"
                :style="{ width: `${row.widthPct}%` }"
              >
                {{ money(row.costAvoided) }}
              </div>
            </div>
          </div>
        </div>
      </div>

      <div class="overflow-hidden rounded-xl border border-slate-200 bg-white">
        <table class="min-w-full divide-y divide-slate-200 text-sm">
          <thead class="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-500">
            <tr>
              <th class="px-4 py-3">
                Workflow
              </th>
              <th class="px-4 py-3">
                Runs
              </th>
              <th class="px-4 py-3">
                Time saved
              </th>
              <th class="px-4 py-3">
                Cost avoided
              </th>
              <th class="px-4 py-3">
                ROI %
              </th>
              <th class="px-4 py-3" />
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-100">
            <tr
              v-for="row in summary.byWorkflow"
              :key="row.workflowDefinitionId"
              class="hover:bg-slate-50"
            >
              <td class="px-4 py-3 font-medium text-slate-800">
                {{ row.workflowName }}
              </td>
              <td class="px-4 py-3 text-slate-600">
                {{ row.successfulRuns }}/{{ row.runs }}
              </td>
              <td class="px-4 py-3 text-slate-600">
                <span v-if="row.configured">{{ timeSaved(row.timeSavedMinutes) }}</span>
                <span
                  v-else
                  class="text-slate-300"
                >—</span>
              </td>
              <td class="px-4 py-3 text-slate-600">
                <span v-if="row.configured">{{ money(row.costAvoided) }}</span>
                <span
                  v-else
                  class="text-slate-300"
                >—</span>
              </td>
              <td class="px-4 py-3 text-slate-600">
                <span v-if="row.configured">{{ row.roiPercentage }}%</span>
                <span
                  v-else
                  class="text-slate-300"
                >—</span>
              </td>
              <td class="px-4 py-3 text-right">
                <button
                  type="button"
                  class="rounded border border-slate-300 px-2.5 py-1 text-xs font-medium text-slate-600 hover:bg-slate-50"
                  @click="openConfig(row)"
                >
                  {{ row.configured ? 'Edit' : 'Configure' }}
                </button>
              </td>
            </tr>
            <tr v-if="summary.byWorkflow.length === 0">
              <td
                colspan="6"
                class="px-4 py-8 text-center text-sm text-slate-400"
              >
                No runs in this period yet. Trigger workflows and configure their ROI baseline to see value here.
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>

    <RoiConfigModal
      v-model="configOpen"
      :workspace-id="workspaceId"
      :workflow-id="configWorkflowId"
      :workflow-name="configWorkflowName"
      @saved="load"
    />
  </div>
</template>
