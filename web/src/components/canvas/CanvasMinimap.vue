<template>
  <div
    ref="rootRef"
    class="absolute bottom-4 right-4 w-36 h-28 bg-white border border-gray-200 rounded-lg overflow-hidden shadow-lg opacity-90 hover:opacity-100 transition-opacity select-none touch-none"
    :class="dataUrl ? (dragging ? 'cursor-grabbing' : 'cursor-pointer') : 'cursor-default'"
    role="button"
    tabindex="-1"
    aria-label="Minimap — click or drag to move the canvas view"
    title="Click or drag to move the view"
    @pointerdown="onPointerDown"
    @pointermove="onPointerMove"
    @pointerup="onPointerUp"
    @pointercancel="onPointerUp"
  >
    <img
      v-if="dataUrl"
      :src="dataUrl"
      class="w-full h-full object-contain pointer-events-none"
      alt=""
      draggable="false"
    >
    <div
      v-else
      class="w-full h-full flex items-center justify-center px-2 text-center"
    >
      <span class="text-gray-400 text-[10px] leading-tight">Add nodes to see an overview here</span>
    </div>
    <div
      v-if="dataUrl && viewRect"
      class="absolute border-2 border-primary-500 bg-primary-500/10 rounded-sm pointer-events-none"
      :style="{
        left: `${viewRect.left}px`,
        top: `${viewRect.top}px`,
        width: `${viewRect.width}px`,
        height: `${viewRect.height}px`,
      }"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, watch, onUnmounted } from 'vue';
import type { Core } from 'cytoscape';
import { useDebounceFn } from '@vueuse/core';
import {
  computeMinimapTransform,
  minimapToModel,
  panToCenter,
  viewportToMinimapRect,
  type MinimapRect,
  type MinimapTransform,
} from '@/utils/canvas.util';

const props = defineProps<{ cy: Core | null; visible: boolean }>();

const RENDER_DEBOUNCE_MS = 250;
// Render the thumbnail at 2x the box for crisp output on HiDPI screens.
const RENDER_SCALE = 2;

const rootRef = ref<HTMLDivElement | null>(null);
const dataUrl = ref('');
const viewRect = ref<MinimapRect | null>(null);
const dragging = ref(false);
let transform: MinimapTransform | null = null;
let rafId = 0;

function boxSize(): { w: number; h: number } {
  const el = rootRef.value;
  // clientWidth excludes the border, matching the image's content box.
  return { w: el?.clientWidth || 144, h: el?.clientHeight || 112 };
}

function updateViewRect() {
  const cy = props.cy;
  if (!cy || !transform) { viewRect.value = null; return; }
  const ext = cy.extent();
  viewRect.value = viewportToMinimapRect({ x1: ext.x1, y1: ext.y1, w: ext.w, h: ext.h }, transform);
}

function renderNow() {
  const cy = props.cy;
  if (!cy || !props.visible) return;
  if (cy.elements().length === 0) {
    dataUrl.value = '';
    transform = null;
    viewRect.value = null;
    return;
  }
  const { w, h } = boxSize();
  try {
    // png({ full: true }) draws the elements' bounding box; mirror that box for the mapping.
    const bb = cy.elements().boundingBox();
    dataUrl.value = cy.png({ output: 'base64uri', full: true, maxWidth: w * RENDER_SCALE, maxHeight: h * RENDER_SCALE, bg: '#F8FAFC' });
    transform = computeMinimapTransform({ x1: bb.x1, y1: bb.y1, w: bb.w, h: bb.h }, w, h);
    updateViewRect();
  } catch {
    // Renderer not ready yet (e.g. container still 0x0) — the next change re-renders.
  }
}

const renderDebounced = useDebounceFn(renderNow, RENDER_DEBOUNCE_MS);

function onViewport() {
  // The rectangle is a cheap calculation; coalesce to at most one update per frame.
  if (rafId) return;
  rafId = requestAnimationFrame(() => { rafId = 0; updateViewRect(); });
}

const GRAPH_EVENTS = 'add remove position data layoutstop';

function attach(cy: Core) {
  cy.on(GRAPH_EVENTS, renderDebounced);
  cy.on('viewport resize', onViewport);
  void renderDebounced();
}

function detach(cy: Core) {
  cy.off(GRAPH_EVENTS, renderDebounced);
  cy.off('viewport resize', onViewport);
}

watch(() => props.cy, (next, prev) => {
  if (prev) detach(prev);
  if (next) attach(next);
}, { immediate: true });

watch(() => props.visible, (v) => { if (v) void renderDebounced(); });

onUnmounted(() => {
  if (props.cy) detach(props.cy);
  if (rafId) cancelAnimationFrame(rafId);
});

// ── Click / drag to pan ──────────────────────────────────────────────────────

function panToPointer(e: PointerEvent) {
  const cy = props.cy;
  const el = rootRef.value;
  if (!cy || !el || !transform) return;
  const rect = el.getBoundingClientRect();
  const local = { x: e.clientX - rect.left - el.clientLeft, y: e.clientY - rect.top - el.clientTop };
  const model = minimapToModel(local, transform);
  cy.pan(panToCenter(model, cy.zoom(), cy.width(), cy.height()));
}

function onPointerDown(e: PointerEvent) {
  if (e.button !== 0 || !transform) return;
  e.preventDefault();
  dragging.value = true;
  try { rootRef.value?.setPointerCapture(e.pointerId); } catch { /* capture unsupported */ }
  panToPointer(e);
}

function onPointerMove(e: PointerEvent) {
  if (dragging.value) panToPointer(e);
}

function onPointerUp(e: PointerEvent) {
  if (!dragging.value) return;
  dragging.value = false;
  try { rootRef.value?.releasePointerCapture(e.pointerId); } catch { /* already released */ }
}

defineExpose({ refresh: renderDebounced });
</script>
