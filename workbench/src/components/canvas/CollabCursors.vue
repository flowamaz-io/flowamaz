<script setup lang="ts">
import { computed } from 'vue';
import type { CanvasCollabPeer as CollabPeerState } from '../../canvasCollab';

// Other people's pointers on the canvas. Cursors are shared in canvas (model) coordinates, so they
// stay on the same node whatever each person's pan and zoom.
const props = defineProps<{
  peers: CollabPeerState[];
  viewport: { zoom: number; pan: { x: number; y: number } };
}>();

const cursors = computed(() =>
  props.peers
    .filter((p) => p.cursor)
    .map((p) => ({
      id: p.clientId,
      name: p.user.name,
      color: p.user.color,
      left: props.viewport.pan.x + (p.cursor?.x ?? 0) * props.viewport.zoom,
      top: props.viewport.pan.y + (p.cursor?.y ?? 0) * props.viewport.zoom,
    })),
);
</script>

<template>
  <div
    class="pointer-events-none absolute inset-0 overflow-hidden"
    data-testid="collab-cursors"
  >
    <div
      v-for="c in cursors"
      :key="c.id"
      class="absolute transition-transform duration-75"
      :style="{ transform: `translate(${c.left}px, ${c.top}px)` }"
      :data-testid="`collab-cursor-${c.id}`"
    >
      <svg
        width="16"
        height="16"
        viewBox="0 0 16 16"
        aria-hidden="true"
      >
        <path
          d="M1 1 L1 13 L4.5 9.5 L7 15 L9 14 L6.5 8.5 L11.5 8.5 Z"
          :fill="c.color"
          stroke="white"
          stroke-width="1"
        />
      </svg>
      <span
        class="ml-3 -mt-1 inline-block whitespace-nowrap rounded px-1.5 py-0.5 text-[11px] font-medium text-white shadow-sm"
        :style="{ backgroundColor: c.color }"
      >{{ c.name }}</span>
    </div>
  </div>
</template>
