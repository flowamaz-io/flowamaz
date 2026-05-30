<template>
  <div class="w-16 bg-[#F1F5F9] border-r border-[#E5E7EB] flex flex-col items-center py-3 gap-2 select-none">
    <div class="text-xs text-[#374151] mb-1 font-medium tracking-wide">
      NODES
    </div>
    <div
      v-for="item in paletteItems"
      :key="item.type"
      :title="item.label"
      draggable="true"
      class="w-12 h-12 rounded-lg flex flex-col items-center justify-center cursor-grab active:cursor-grabbing gap-0.5 transition-all hover:scale-105"
      :style="{ backgroundColor: item.color + '22', borderColor: item.color, borderWidth: '1px', borderStyle: 'solid' }"
      @dragstart="onDragStart($event, item.type)"
    >
      <component
        :is="item.icon"
        class="w-5 h-5"
        :style="{ color: item.color }"
      />
      <span
        class="text-[9px] font-medium"
        :style="{ color: item.color }"
      >{{ item.shortLabel }}</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import {
  Zap, Play, Brain, UserCheck, GitBranch, CircleStop, StickyNote,
} from 'lucide-vue-next';
import { NODE_VISUALS } from '@/types/canvas.types';
import type { NodeType } from '@/types/canvas.types';

const paletteItems: { type: NodeType; label: string; shortLabel: string; color: string; icon: unknown }[] = [
  { type: 'trigger',    label: 'Trigger',     shortLabel: 'Trig',  color: NODE_VISUALS.trigger.color,      icon: Zap },
  { type: 'action',     label: 'Action',      shortLabel: 'Act',   color: NODE_VISUALS.action.color,       icon: Play },
  { type: 'ai',         label: 'AI',          shortLabel: 'AI',    color: NODE_VISUALS.ai.color,           icon: Brain },
  { type: 'human-gate', label: 'Human Gate',  shortLabel: 'Gate',  color: NODE_VISUALS['human-gate'].color, icon: UserCheck },
  { type: 'router',     label: 'Router',      shortLabel: 'Route', color: NODE_VISUALS.router.color,       icon: GitBranch },
  { type: 'end',        label: 'End',         shortLabel: 'End',   color: NODE_VISUALS.end.color,          icon: CircleStop },
  { type: 'annotation', label: 'Annotation',  shortLabel: 'Note',  color: NODE_VISUALS.annotation.color,  icon: StickyNote },
];

function onDragStart(e: DragEvent, type: NodeType) {
  e.dataTransfer?.setData('node-type', type);
}
</script>
