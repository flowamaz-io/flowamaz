<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { storeToRefs } from 'pinia';
import { useRouter } from 'vue-router';
import GettingStartedChecklist from '@/components/onboarding/GettingStartedChecklist.vue';
import FmWorkflowWeather from '@/components/dashboard/FmWorkflowWeather.vue';
import { useAuth } from '@/composables/useAuth';
import { useWorkspace } from '@/composables/useWorkspace';
import { useWorkflowStore } from '@/stores/workflow.store';
import { analyticsService } from '@/services/analytics.service';
import { CREATION_METHODS } from '@/utils/constants';
import type { WeatherResponse, WorkspaceRoiSummary } from '@/types';

const { user } = useAuth();
const ws = useWorkspace();
const { current } = ws;

// Use name, falling back to slug, so the subtitle never reads "happening in ." with no name.
const workspaceLabel = computed(() => current.value?.name || current.value?.slug || 'your workspace');
const workflowStore = useWorkflowStore();
const { workflows } = storeToRefs(workflowStore);
const router = useRouter();

const weather = ref<WeatherResponse | null>(null);
const roi = ref<WorkspaceRoiSummary | null>(null);

const costAvoided = computed(() => {
  if (!roi.value) return '--';
  const currency = roi.value.byWorkflow.find((w) => w.configured)?.currency ?? 'MYR';
  return new Intl.NumberFormat('en-US', { style: 'currency', currency, maximumFractionDigits: 0 }).format(roi.value.totalCostAvoided);
});

const metrics = computed(() => [
  { label: 'Total workflows', value: workflows.value.length.toString() },
  { label: 'Active instances', value: (weather.value?.activeInstances ?? 0).toString() },
  { label: 'Runs this month', value: (weather.value?.runsThisMonth ?? 0).toString() },
  { label: 'Cost avoided', value: costAvoided.value },
]);

function openCreationMethod(method: string): void {
  if (method) {
    router.push({ name: 'workflow-new', query: { method } });
  } else {
    router.push({ name: 'workflow-new' });
  }
}

onMounted(async () => {
  await Promise.allSettled([
    ws.loadMembers(),
    workflowStore.loadWorkflows(),
  ]);
  const wsId = ws.currentWorkspaceId.value;
  if (wsId) {
    try {
      weather.value = await analyticsService.weather(wsId);
    } catch {
      weather.value = null;
    }
    try {
      const now = new Date();
      const from = new Date(now.getFullYear(), now.getMonth(), 1).toISOString();
      roi.value = await analyticsService.roi(wsId, from, now.toISOString());
    } catch {
      roi.value = null;
    }
  }
});
</script>

<template>
  <div class="mx-auto max-w-5xl space-y-6 p-6">
    <header>
      <h1 class="text-2xl font-semibold text-slate-900">
        Welcome back, {{ user?.name?.split(' ')[0] ?? 'there' }}
      </h1>
      <p class="text-sm text-slate-500">
        Here's what's happening in {{ workspaceLabel }}.
      </p>
    </header>

    <GettingStartedChecklist :runs-this-month="weather?.runsThisMonth ?? 0" />

    <!-- Metric cards -->
    <div class="grid grid-cols-2 gap-4 lg:grid-cols-4">
      <div
        v-for="m in metrics"
        :key="m.label"
        class="rounded-xl border border-slate-200 bg-white p-5"
      >
        <p class="text-sm text-slate-500">
          {{ m.label }}
        </p>
        <p class="mt-2 text-3xl font-semibold text-slate-900">
          {{ m.value }}
        </p>
      </div>
    </div>

    <FmWorkflowWeather :workflows="weather?.workflows ?? []" />

    <!-- Creation hero -->
    <section class="rounded-xl border border-slate-200 bg-white p-6">
      <h2 class="text-lg font-semibold text-slate-900">
        What would you like to automate?
      </h2>
      <p class="mt-1 text-sm text-slate-500">
        Choose a creation method to build your next workflow.
      </p>
      <div class="mt-5 grid grid-cols-2 gap-3 sm:grid-cols-3">
        <button
          v-for="m in CREATION_METHODS"
          :key="m.id"
          type="button"
          class="flex flex-col items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 py-6 text-center transition-colors hover:border-primary-400 hover:bg-primary-50"
          @click="openCreationMethod(m.method)"
        >
          <component
            :is="m.icon"
            class="h-6 w-6 text-primary-600"
          />
          <span class="text-sm font-medium text-slate-700">{{ m.label }}</span>
        </button>
      </div>
    </section>
  </div>
</template>
