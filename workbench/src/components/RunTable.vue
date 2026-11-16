<script setup lang="ts">
import FmBadge from './FmBadge.vue';
import { fromNow } from '../date';
import { instanceStatusVariant, runDuration, testRunExpiry } from '../runs';
import type { InstanceListItem } from '../types';

// The runs table: one row per run, click to open it.
withDefaults(
  defineProps<{
    runs: InstanceListItem[];
    /** The workflow's name for a run; defaults to the workflow id. */
    workflowName?: (workflowId: string) => string;
    /** Adds the test-run expiry column. */
    showExpiry?: boolean;
  }>(),
  { workflowName: (id: string) => id, showExpiry: false },
);

defineEmits<{ open: [runId: string] }>();
</script>

<template>
  <div class="overflow-hidden rounded-xl border border-slate-200 bg-white">
    <table class="min-w-full divide-y divide-slate-200 text-sm">
      <thead class="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-500">
        <tr>
          <th class="px-4 py-3">
            Instance
          </th>
          <th class="px-4 py-3">
            Workflow
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
            v-if="showExpiry"
            class="px-4 py-3"
          >
            Expiry
          </th>
        </tr>
      </thead>
      <tbody class="divide-y divide-slate-100">
        <tr
          v-for="run in runs"
          :key="run.id"
          class="cursor-pointer hover:bg-slate-50"
          @click="$emit('open', run.id)"
        >
          <td class="px-4 py-3 font-mono text-xs text-primary-600">
            {{ run.id.slice(0, 8) }}
            <span
              v-if="run.isTest"
              class="ml-2 inline-flex items-center rounded px-1.5 py-0.5 text-[10px] font-semibold bg-amber-100 text-amber-700"
            >
              TEST
            </span>
          </td>
          <td class="px-4 py-3 text-slate-700">
            {{ workflowName(run.workflowDefinitionId) }}
          </td>
          <td class="px-4 py-3">
            <FmBadge :variant="instanceStatusVariant(run.status)">
              {{ run.status }}
            </FmBadge>
          </td>
          <td class="px-4 py-3 text-slate-500">
            {{ run.triggerType }}
          </td>
          <td class="px-4 py-3 text-slate-500">
            {{ fromNow(run.startedAt ?? run.createdAt) }}
          </td>
          <td class="px-4 py-3 tabular-nums text-slate-500">
            {{ runDuration(run) }}
          </td>
          <td
            v-if="showExpiry"
            class="px-4 py-3 text-xs text-amber-600"
          >
            {{ testRunExpiry(run.testExpiresAt) }}
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
