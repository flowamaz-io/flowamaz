<script setup lang="ts">
import { computed } from 'vue';

const props = withDefaults(
  defineProps<{
    /** Colour variant. */
    variant?: 'primary' | 'accent' | 'amber' | 'danger' | 'slate' | 'blue';
    /** Your own colour classes instead of a variant (e.g. "bg-teal-100 text-teal-700"). */
    tone?: string;
  }>(),
  { variant: 'slate', tone: undefined },
);

const cls = computed(() => props.tone ?? {
  primary: 'bg-primary-100 text-primary-700',
  accent: 'bg-accent-100 text-accent-700',
  amber: 'bg-amber-100 text-amber-700',
  danger: 'bg-danger-100 text-danger-700',
  slate: 'bg-slate-100 text-slate-600',
  blue: 'bg-blue-100 text-blue-700',
}[props.variant]);
</script>

<template>
  <span :class="['inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium', cls]">
    <slot />
  </span>
</template>
