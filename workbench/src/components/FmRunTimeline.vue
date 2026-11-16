<script setup lang="ts">
import { computed } from 'vue';
import type { TimelineNode, TimelineResponse } from '../types';

const props = defineProps<{ timeline: TimelineResponse }>();

interface Bar {
  node: TimelineNode;
  leftPct: number;
  widthPct: number;
  colorClass: string;
}

const STATUS_COLOUR: Record<string, string> = {
  Completed: 'bg-teal-500',
  Failed: 'bg-danger-500',
  Running: 'bg-blue-500 animate-pulse',
  Waiting: 'bg-amber-500',
  Compensating: 'bg-orange-500',
  Skipped: 'bg-slate-300',
  Pending: 'bg-slate-300',
};

// The scale is the run's span (latest step end from the run's start), not the sum of step durations:
// offsets are measured from the run's start, so summing durations let bars run past the card's edge
// (a step waiting on a gate, retry waits between tries).
const bars = computed<Bar[]>(() => {
  const span = Math.max(
    props.timeline.totalDurationMs,
    ...props.timeline.nodes.map((n) => n.offsetMs + n.durationMs),
    0,
  );
  return props.timeline.nodes.map((node) => {
    const leftPct = span > 0 ? Math.min(100, (node.offsetMs / span) * 100) : 0;
    const widthPct = span > 0 ? Math.min(100 - leftPct, (node.durationMs / span) * 100) : 0;
    return { node, leftPct, widthPct, colorClass: STATUS_COLOUR[node.status] ?? 'bg-slate-300' };
  });
});

/** 850ms, 15.7s, 2m 5s — readable at the end of the row. */
function durationLabel(ms: number): string {
  if (ms < 1000) return `${ms}ms`;
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)}s`;
  return `${Math.floor(ms / 60_000)}m ${Math.round((ms % 60_000) / 1000)}s`;
}

function tooltip(node: TimelineNode): string {
  return `${node.label} · ${node.status} · ${durationLabel(node.durationMs)} · retries ${node.retryCount}`;
}
</script>

<template>
  <div class="space-y-1.5">
    <div
      v-for="bar in bars"
      :key="bar.node.nodeId"
      class="flex items-center gap-3"
    >
      <div class="flex w-48 shrink-0 items-center gap-2">
        <span :class="['h-2.5 w-2.5 shrink-0 rounded-full', bar.colorClass]" />
        <div class="min-w-0">
          <p class="truncate text-sm font-medium text-slate-800">
            {{ bar.node.label }}
          </p>
          <p class="text-xs text-slate-400">
            {{ bar.node.nodeType }}
          </p>
        </div>
      </div>
      <div class="relative h-5 flex-1 overflow-hidden rounded bg-slate-100">
        <div
          :data-node-id="bar.node.nodeId"
          :class="['absolute top-0 h-5 min-w-[2px] rounded', bar.colorClass]"
          :style="{ left: `${bar.leftPct}%`, width: `${bar.widthPct}%` }"
          :title="tooltip(bar.node)"
        />
      </div>
      <span class="w-16 shrink-0 text-right text-xs tabular-nums text-slate-500">{{ durationLabel(bar.node.durationMs) }}</span>
    </div>
  </div>
</template>
