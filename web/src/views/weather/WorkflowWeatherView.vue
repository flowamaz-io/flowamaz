<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { analyticsService } from '../../services/analytics.service';
import WeatherInsightsSidebar from '../../components/weather/WeatherInsightsSidebar.vue';
import type { WeatherResponse, WorkflowWeather } from '../../types/workflow';

const route = useRoute();
const router = useRouter();
const workspaceId = route.params.workspaceId as string;

const data = ref<WeatherResponse | null>(null);
const loading = ref(true);
const error = ref<string | null>(null);
const sidebarOpen = ref(false);
const lastUpdated = ref<Date | null>(null);
let refreshTimer: ReturnType<typeof setInterval> | null = null;

async function load() {
  try {
    data.value = await analyticsService.weather(workspaceId);
    lastUpdated.value = new Date();
    error.value = null;
  } catch (e: unknown) {
    error.value = `Failed to load weather data. ${e instanceof Error ? e.message : 'Please refresh.'}`;
  } finally {
    loading.value = false;
  }
}

function formatLastUpdated() {
  if (!lastUpdated.value) return '';
  const seconds = Math.floor((Date.now() - lastUpdated.value.getTime()) / 1000);
  if (seconds < 60) return 'Just now';
  const minutes = Math.floor(seconds / 60);
  return `${minutes}m ago`;
}

const statusCounts = computed(() => ({
  green: data.value?.workflows.filter(w => w.status === 'green').length ?? 0,
  yellow: data.value?.workflows.filter(w => w.status === 'yellow').length ?? 0,
  orange: data.value?.workflows.filter(w => w.status === 'orange').length ?? 0,
  red: data.value?.workflows.filter(w => w.status === 'red').length ?? 0,
}));

const STATUS_STYLES: Record<string, string> = {
  green: 'bg-teal-500',
  yellow: 'bg-amber-400',
  orange: 'bg-orange-500',
  red: 'bg-red-500 animate-pulse',
};
const STATUS_BORDER: Record<string, string> = {
  green: 'border-teal-200 bg-teal-50',
  yellow: 'border-amber-200 bg-amber-50',
  orange: 'border-orange-200 bg-orange-50',
  red: 'border-red-200 bg-red-50',
};

function navigateToWorkflow(wf: WorkflowWeather) {
  router.push({ name: 'workflow-detail', params: { workspaceId, id: wf.workflowId } });
}

onMounted(() => {
  load();
  refreshTimer = setInterval(load, 60_000);
});
onUnmounted(() => { if (refreshTimer) clearInterval(refreshTimer); });
</script>

<template>
  <div class="min-h-screen bg-neutral-50">
    <!-- Header -->
    <div class="bg-white border-b border-neutral-200 px-6 py-4 flex items-center gap-4">
      <div class="flex-1">
        <h1 class="text-xl font-bold text-neutral-900">Workflow Weather</h1>
        <p v-if="lastUpdated" class="text-xs text-neutral-400 mt-0.5">Last updated: {{ formatLastUpdated() }}</p>
      </div>
      <button
        class="text-sm bg-neutral-100 hover:bg-neutral-200 text-neutral-700 rounded-lg px-3 py-1.5"
        @click="load"
      >Refresh</button>
    </div>

    <div class="max-w-7xl mx-auto px-6 py-6 space-y-6">
      <!-- Loading -->
      <div v-if="loading" class="flex justify-center py-20">
        <div class="w-8 h-8 border-4 border-violet-500 border-t-transparent rounded-full animate-spin" />
      </div>

      <!-- Error -->
      <div v-else-if="error" class="bg-red-50 border border-red-200 rounded-xl px-5 py-4 text-sm text-red-700">
        {{ error }}
      </div>

      <template v-else-if="data">
        <!-- Summary strip -->
        <div class="grid grid-cols-4 gap-4">
          <div v-for="(count, status) in statusCounts" :key="status"
               class="bg-white rounded-xl border border-neutral-200 p-4 flex items-center gap-3">
            <div :class="['w-4 h-4 rounded-full shrink-0', STATUS_STYLES[status]]" />
            <div>
              <p class="text-2xl font-bold text-neutral-900">{{ count }}</p>
              <p class="text-xs text-neutral-500 capitalize">{{ status }}</p>
            </div>
          </div>
        </div>

        <!-- Workflow cards grid -->
        <div v-if="!data.workflows.length" class="text-center py-16 text-neutral-400">
          <p class="text-lg font-medium">No workflows yet</p>
          <p class="text-sm mt-1">Create your first workflow to see its health here.</p>
        </div>

        <div v-else class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
          <div
            v-for="wf in data.workflows"
            :key="wf.workflowId"
            :class="['rounded-2xl border-2 p-4 cursor-pointer transition-all hover:shadow-md group', STATUS_BORDER[wf.status] ?? 'border-neutral-200 bg-white']"
            @click="navigateToWorkflow(wf)"
          >
            <!-- Header -->
            <div class="flex items-start justify-between mb-3">
              <p class="font-semibold text-neutral-900 text-sm leading-tight">{{ wf.workflowName }}</p>
              <div class="flex items-center gap-1.5 shrink-0 ml-2">
                <div :class="['w-3 h-3 rounded-full', STATUS_STYLES[wf.status]]" />
                <button
                  v-if="wf.pendingInsights > 0"
                  class="text-amber-500 hover:text-amber-700 text-xs flex items-center gap-0.5"
                  @click.stop="sidebarOpen = true"
                >
                  🔔 {{ wf.pendingInsights }}
                </button>
              </div>
            </div>

            <!-- Stats -->
            <div class="space-y-1 text-xs text-neutral-600">
              <div class="flex justify-between">
                <span>Active instances</span>
                <span class="font-medium">{{ wf.activeInstances }}</span>
              </div>
              <div class="flex justify-between">
                <span>Failed last hour</span>
                <span :class="wf.failedLastHour > 0 ? 'font-medium text-red-600' : 'font-medium'">{{ wf.failedLastHour }}</span>
              </div>
              <div class="flex justify-between">
                <span>SLA compliance</span>
                <span :class="['font-medium', wf.slaCompliancePct >= 95 ? 'text-teal-600' : wf.slaCompliancePct >= 80 ? 'text-amber-600' : 'text-red-600']">
                  {{ wf.slaCompliancePct }}%
                </span>
              </div>
            </div>

            <!-- Insight preview on hover -->
            <p v-if="wf.latestInsight" class="mt-3 text-xs text-neutral-500 italic line-clamp-2 group-hover:line-clamp-none transition-all">
              {{ wf.latestInsight }}
            </p>
          </div>
        </div>
      </template>
    </div>

    <!-- Insights sidebar -->
    <WeatherInsightsSidebar
      :workspace-id="workspaceId"
      :open="sidebarOpen"
      @close="sidebarOpen = false"
    />
  </div>
</template>
