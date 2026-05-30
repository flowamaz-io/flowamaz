<script setup lang="ts">
import { nextTick, ref } from 'vue';
import { HelpCircle } from 'lucide-vue-next';

// Contextual help tooltip. A small "?" trigger reveals a short explanation.
// Opens on hover and on click/focus (keyboard-operable). Positions above or below
// the trigger based on the room available, so the bubble never clips off-screen.
withDefaults(
  defineProps<{
    text: string;
    label?: string;
  }>(),
  { label: undefined },
);

const open = ref(false);
const placement = ref<'top' | 'bottom'>('bottom');
const trigger = ref<HTMLButtonElement | null>(null);

function decidePlacement(): void {
  const el = trigger.value;
  if (!el) return;
  const rect = el.getBoundingClientRect();
  const spaceBelow = window.innerHeight - rect.bottom;
  // Reserve ~120px for the bubble; flip to top when there is not enough room below.
  placement.value = spaceBelow < 120 ? 'top' : 'bottom';
}

async function show(): Promise<void> {
  decidePlacement();
  open.value = true;
  await nextTick();
}

function hide(): void {
  open.value = false;
}

function toggle(): void {
  if (open.value) {
    hide();
  } else {
    void show();
  }
}
</script>

<template>
  <span class="relative inline-flex">
    <button
      ref="trigger"
      type="button"
      class="inline-flex items-center justify-center rounded-full text-slate-400 transition-colors hover:text-slate-600 focus:text-slate-600 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary-300"
      :aria-label="label ?? 'Help'"
      :aria-expanded="open"
      @mouseenter="show"
      @mouseleave="hide"
      @focus="show"
      @blur="hide"
      @click="toggle"
    >
      <HelpCircle class="h-3.5 w-3.5" />
    </button>
    <span
      v-if="open"
      role="tooltip"
      :class="[
        'absolute left-1/2 z-50 w-56 -translate-x-1/2 rounded-lg bg-slate-900 px-3 py-2 text-xs font-normal leading-relaxed text-white shadow-lg',
        placement === 'bottom' ? 'top-full mt-2' : 'bottom-full mb-2',
      ]"
    >
      {{ text }}
    </span>
  </span>
</template>
