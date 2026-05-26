<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { storeToRefs } from 'pinia';
import GettingStartedChecklist from '@/components/onboarding/GettingStartedChecklist.vue';
import FmWorkflowWeather from '@/components/dashboard/FmWorkflowWeather.vue';
import { useAuth } from '@/composables/useAuth';
import { useWorkspace } from '@/composables/useWorkspace';
import { useWorkflowStore } from '@/stores/workflow.store';
import { analyticsService } from '@/services/analytics.service';
import { CREATION_METHODS } from '@/utils/constants';
import type { WeatherResponse } from '@/types';

const { user } = useAuth();
const ws = useWorkspace();
const workflowStore = useWorkflowStore();
const { workflows } = storeToRefs(workflowStore);
const { members } = ws;

const weather = ref<WeatherResponse | null>(null);

const metrics = computed(() => [
  { label: 'Total workflows', value: workflows.value.length.toString() },
  { label: 'Active instances', value: (weather.value?.activeInstances ?? 0).toString() },
  { label: 'Runs this month', value: (weather.value?.runsThisMonth ?? 0).toString() },
  { label: 'Team members', value: members.value.length.toString() },
]);

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
        Here's what's happening in {{ ws.current.value?.name ?? 'your workspace' }}.
      </p>
    </header>

    <GettingStartedChecklist />

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
        Drawing-board creation methods unlock in Phase 3 — the engine and runs are live now.
      </p>
      <div class="mt-5 grid grid-cols-2 gap-3 sm:grid-cols-3">
        <div
          v-for="m in CREATION_METHODS"
          :key="m.id"
          class="flex cursor-not-allowed flex-col items-center gap-2 rounded-xl border border-dashed border-slate-200 bg-slate-50 px-3 py-6 text-center opacity-70"
        >
          <component
            :is="m.icon"
            class="h-6 w-6 text-slate-400"
          />
          <span class="text-sm font-medium text-slate-500">{{ m.label }}</span>
        </div>
      </div>
    </section>
  </div>
</template>
