<script setup lang="ts">
import { computed } from 'vue';
import { ArrowLeft, ArrowRight, X } from 'lucide-vue-next';

// A single product-tour step: the dark backdrop with a cutout spotlight around the
// target element, a tooltip card positioned relative to the spotlight, progress dots,
// and the navigation buttons. Pure presentation — ProductTour owns the state machine.

export interface SpotlightRect {
  top: number;
  left: number;
  width: number;
  height: number;
}

const props = defineProps<{
  title: string;
  body: string;
  /** null = no element to highlight (centre the card, dim the whole screen). */
  rect: SpotlightRect | null;
  stepIndex: number;
  stepCount: number;
  isFirst: boolean;
  isLast: boolean;
  /** Optional secondary action label (e.g. "Try it now", "Start from template"). */
  primaryActionLabel?: string;
}>();

defineEmits<{
  next: [];
  back: [];
  skip: [];
  primaryAction: [];
}>();

const PAD = 8;

// The four overlay panels around the spotlight create a "cutout" without SVG masks.
const panels = computed(() => {
  const r = props.rect;
  if (!r) return null;
  const top = Math.max(r.top - PAD, 0);
  const left = Math.max(r.left - PAD, 0);
  const width = r.width + PAD * 2;
  const height = r.height + PAD * 2;
  return { top, left, width, height };
});

// Tooltip card placement: below the spotlight when there is room, otherwise above.
// When there is no target, centre it.
const cardStyle = computed(() => {
  const p = panels.value;
  if (!p) {
    return { top: '50%', left: '50%', transform: 'translate(-50%, -50%)' };
  }
  const below = p.top + p.height + 16;
  const spaceBelow = window.innerHeight - (p.top + p.height);
  const left = Math.min(Math.max(p.left, 16), window.innerWidth - 360);
  if (spaceBelow > 220) {
    return { top: `${below}px`, left: `${left}px` };
  }
  return { top: `${Math.max(p.top - 16, 16)}px`, left: `${left}px`, transform: 'translateY(-100%)' };
});
</script>

<template>
  <div class="fixed inset-0 z-[100]">
    <!-- Spotlight cutout via four dark panels; or a single full dim when no target. -->
    <template v-if="panels">
      <div
        class="absolute left-0 right-0 top-0 bg-slate-900/70"
        :style="{ height: `${panels.top}px` }"
      />
      <div
        class="absolute left-0 bg-slate-900/70"
        :style="{ top: `${panels.top}px`, width: `${panels.left}px`, height: `${panels.height}px` }"
      />
      <div
        class="absolute right-0 bg-slate-900/70"
        :style="{ top: `${panels.top}px`, left: `${panels.left + panels.width}px`, height: `${panels.height}px` }"
      />
      <div
        class="absolute bottom-0 left-0 right-0 bg-slate-900/70"
        :style="{ top: `${panels.top + panels.height}px` }"
      />
      <!-- Highlight ring around the spotlit element. -->
      <div
        class="pointer-events-none absolute rounded-xl ring-2 ring-primary-400 ring-offset-2 ring-offset-transparent"
        :style="{ top: `${panels.top}px`, left: `${panels.left}px`, width: `${panels.width}px`, height: `${panels.height}px` }"
      />
    </template>
    <div
      v-else
      class="absolute inset-0 bg-slate-900/70"
    />

    <!-- Tooltip card -->
    <div
      class="absolute w-[22rem] max-w-[calc(100vw-2rem)] rounded-xl border border-slate-200 bg-white p-5 shadow-2xl"
      :style="cardStyle"
      role="dialog"
      aria-modal="true"
      :aria-label="title"
    >
      <button
        type="button"
        class="absolute right-3 top-3 text-slate-400 hover:text-slate-600"
        aria-label="Skip tour"
        @click="$emit('skip')"
      >
        <X class="h-4 w-4" />
      </button>

      <h3 class="pr-6 text-base font-semibold text-slate-900">
        {{ title }}
      </h3>
      <p class="mt-2 text-sm leading-relaxed text-slate-600">
        {{ body }}
      </p>

      <slot name="extra" />

      <!-- Progress dots ●●○○○ -->
      <div
        class="mt-5 flex items-center justify-center gap-1.5"
        :aria-label="`Step ${stepIndex + 1} of ${stepCount}`"
      >
        <span
          v-for="i in stepCount"
          :key="i"
          :class="[
            'h-1.5 w-1.5 rounded-full',
            i - 1 <= stepIndex ? 'bg-primary-600' : 'bg-slate-300',
          ]"
        />
      </div>

      <div class="mt-4 flex items-center justify-between gap-2">
        <button
          v-if="!isFirst"
          type="button"
          class="inline-flex items-center gap-1 rounded-lg px-3 py-2 text-sm font-medium text-slate-500 hover:text-slate-700"
          @click="$emit('back')"
        >
          <ArrowLeft class="h-4 w-4" />
          Back
        </button>
        <button
          v-else
          type="button"
          class="rounded-lg px-3 py-2 text-sm font-medium text-slate-500 hover:text-slate-700"
          @click="$emit('skip')"
        >
          Skip tour
        </button>

        <div class="flex items-center gap-2">
          <button
            v-if="primaryActionLabel"
            type="button"
            class="rounded-lg border border-primary-300 px-3 py-2 text-sm font-medium text-primary-700 hover:bg-primary-50"
            @click="$emit('primaryAction')"
          >
            {{ primaryActionLabel }}
          </button>
          <button
            type="button"
            class="inline-flex items-center gap-1 rounded-lg bg-primary-600 px-4 py-2 text-sm font-semibold text-white hover:bg-primary-700"
            @click="$emit('next')"
          >
            {{ isLast ? 'Explore on my own' : 'Next' }}
            <ArrowRight
              v-if="!isLast"
              class="h-4 w-4"
            />
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
