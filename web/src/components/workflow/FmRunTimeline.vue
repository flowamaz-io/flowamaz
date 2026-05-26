<script setup lang="ts">
import { computed } from 'vue';
import type { TimelineNode, TimelineResponse } from '@/types';

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

const bars = computed<Bar[]>(() => {
  const total = props.timeline.totalDurationMs;
  return props.timeline.nodes.map((node) => ({
    node,
    leftPct: total > 0 ? (node.offsetMs / total) * 100 : 0,
    widthPct: total > 0 ? (node.durationMs / total) * 100 : 0,
    colorClass: STATUS_COLOUR[node.status] ?? 'bg-slate-300',
  }));
});

function tooltip(node: TimelineNode): string {
  return `${node.label} · ${node.status} · ${node.durationMs}ms · retries ${node.retryCount}`;
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
      <div class="relative h-5 flex-1 rounded bg-slate-100">
        <div
          :data-node-id="bar.node.nodeId"
          :class="['absolute top-0 h-5 min-w-[2px] rounded', bar.colorClass]"
          :style="{ left: `${bar.leftPct}%`, width: `${bar.widthPct}%` }"
          :title="tooltip(bar.node)"
        />
      </div>
      <span class="w-16 shrink-0 text-right text-xs tabular-nums text-slate-500">{{ bar.node.durationMs }}ms</span>
    </div>
  </div>
</template>
