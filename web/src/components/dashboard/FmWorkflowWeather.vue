<script setup lang="ts">
import { useRouter } from 'vue-router';
import type { WeatherStatus, WorkflowWeather } from '@/types';

defineProps<{ workflows: WorkflowWeather[] }>();

const router = useRouter();

const DOT: Record<WeatherStatus, string> = {
  green: 'bg-teal-500',
  yellow: 'bg-amber-400',
  orange: 'bg-orange-500',
  red: 'bg-danger-500',
};
</script>

<template>
  <section>
    <h2 class="mb-3 text-lg font-semibold text-slate-900">
      Workflow weather
    </h2>
    <div
      v-if="workflows.length === 0"
      class="rounded-xl border border-dashed border-slate-300 bg-white p-6 text-center text-sm text-slate-400"
    >
      No workflows yet. Once you publish and run workflows, their health appears here.
    </div>
    <div
      v-else
      class="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3"
    >
      <button
        v-for="wf in workflows"
        :key="wf.workflowId"
        type="button"
        class="rounded-xl border border-slate-200 bg-white p-4 text-left transition-shadow hover:shadow-sm"
        @click="router.push(`/workflows/${wf.workflowId}`)"
      >
        <div class="flex items-center justify-between">
          <span class="truncate font-medium text-slate-800">{{ wf.workflowName }}</span>
          <span :class="['h-3 w-3 shrink-0 rounded-full', DOT[wf.status]]" />
        </div>
        <div class="mt-2 flex items-center gap-4 text-xs text-slate-500">
          <span>{{ wf.activeInstances }} active</span>
          <span>SLA {{ wf.slaCompliancePct }}%</span>
          <span v-if="wf.pendingInsights > 0">{{ wf.pendingInsights }} insight{{ wf.pendingInsights === 1 ? '' : 's' }}</span>
        </div>
        <p
          v-if="wf.latestInsight"
          class="mt-2 line-clamp-2 text-xs text-slate-400"
        >
          {{ wf.latestInsight }}
        </p>
      </button>
    </div>
  </section>
</template>
