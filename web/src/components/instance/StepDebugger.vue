<script setup lang="ts">
import { computed, ref } from 'vue';
import { ChevronDown, ChevronRight } from 'lucide-vue-next';
import type { EventResponse, NodeStateResponse } from '@/types';

const props = defineProps<{
  nodeStates: NodeStateResponse[];
  events: EventResponse[];
}>();

interface DebugRow {
  nodeId: string;
  nodeType: string;
  status: string;
  durationMs: number | null;
  input: string | null;
  output: string | null;
  error: string | null;
}

const expanded = ref<Set<string>>(new Set());

function toggle(nodeId: string): void {
  const next = new Set(expanded.value);
  if (next.has(nodeId)) next.delete(nodeId);
  else next.add(nodeId);
  expanded.value = next;
}

function latestEvent(nodeId: string, predicate: (e: EventResponse) => boolean): EventResponse | undefined {
  return [...props.events]
    .filter((e) => e.nodeId === nodeId && predicate(e))
    .sort((a, b) => b.sequenceNumber - a.sequenceNumber)[0];
}

function pretty(raw: string | null | undefined): string | null {
  if (!raw) return null;
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

const rows = computed<DebugRow[]>(() =>
  props.nodeStates.map((state) => {
    const started = latestEvent(state.nodeId, (e) => e.inputSnapshot !== null);
    const completed = latestEvent(state.nodeId, (e) => e.outputSnapshot !== null || e.durationMs !== null);
    const failed = latestEvent(state.nodeId, (e) => e.errorSnapshot !== null);
    return {
      nodeId: state.nodeId,
      nodeType: state.nodeType,
      status: state.status,
      durationMs: completed?.durationMs ?? null,
      input: pretty(started?.inputSnapshot ?? null),
      output: pretty(completed?.outputSnapshot ?? null),
      error: pretty(failed?.errorSnapshot ?? null),
    };
  }),
);

// green (complete), amber (pending/waiting), red (failed), gray (skipped/other).
function dotClass(status: string): string {
  switch (status) {
    case 'Completed':
      return 'bg-teal-500';
    case 'Pending':
    case 'Running':
    case 'Waiting':
      return 'bg-amber-500';
    case 'Failed':
      return 'bg-danger-500';
    case 'Skipped':
      return 'bg-slate-300';
    default:
      return 'bg-slate-300';
  }
}

function durationLabel(ms: number | null): string {
  if (ms === null) return '';
  if (ms < 1000) return `${ms}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}

function hasDetail(row: DebugRow): boolean {
  return row.input !== null || row.output !== null || row.error !== null;
}
</script>

<template>
  <div class="overflow-hidden rounded-xl border border-slate-200 bg-white">
    <ul class="divide-y divide-slate-100">
      <li
        v-for="row in rows"
        :key="row.nodeId"
      >
        <button
          type="button"
          class="flex w-full items-center gap-3 px-4 py-3 text-left hover:bg-slate-50"
          :disabled="!hasDetail(row)"
          @click="toggle(row.nodeId)"
        >
          <component
            :is="expanded.has(row.nodeId) ? ChevronDown : ChevronRight"
            class="h-4 w-4 shrink-0"
            :class="hasDetail(row) ? 'text-slate-400' : 'text-transparent'"
          />
          <span
            class="h-2.5 w-2.5 shrink-0 rounded-full"
            :class="dotClass(row.status)"
          />
          <span class="font-medium text-slate-800">{{ row.nodeId }}</span>
          <span class="text-xs text-slate-400">{{ row.nodeType }}</span>
          <span class="ml-auto text-xs tabular-nums text-slate-500">{{ durationLabel(row.durationMs) }}</span>
          <span class="ml-2 text-xs text-slate-400">{{ row.status }}</span>
        </button>

        <div
          v-if="expanded.has(row.nodeId) && hasDetail(row)"
          class="space-y-3 bg-slate-50 px-10 py-3"
        >
          <div v-if="row.input !== null">
            <p class="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-500">
              Inputs
            </p>
            <pre class="overflow-x-auto rounded-lg bg-white p-3 font-mono text-xs text-slate-700 ring-1 ring-slate-200">{{ row.input }}</pre>
          </div>
          <div v-if="row.output !== null">
            <p class="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-500">
              Outputs
            </p>
            <pre class="overflow-x-auto rounded-lg bg-white p-3 font-mono text-xs text-slate-700 ring-1 ring-slate-200">{{ row.output }}</pre>
          </div>
          <div v-if="row.error !== null">
            <p class="mb-1 text-xs font-semibold uppercase tracking-wide text-danger-600">
              Error
            </p>
            <pre class="overflow-x-auto rounded-lg bg-danger-50 p-3 font-mono text-xs text-danger-700 ring-1 ring-danger-200">{{ row.error }}</pre>
          </div>
        </div>
      </li>

      <li
        v-if="rows.length === 0"
        class="px-4 py-8 text-center text-sm text-slate-400"
      >
        No node activity yet. Trigger or replay this workflow to see per-step inputs and outputs here.
      </li>
    </ul>
  </div>
</template>
