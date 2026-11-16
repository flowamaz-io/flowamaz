<script setup lang="ts">
import { computed } from 'vue';

// Animated gray placeholder for data-fetching states. Renders `count` pulsing bars so a list/table
// or a set of metric cards can show structure while loading instead of a bare spinner.
const props = withDefaults(
  defineProps<{
    count?: number;
    height?: string;
    rounded?: 'md' | 'lg' | 'xl' | 'full';
  }>(),
  { count: 1, height: 'h-4', rounded: 'md' },
);

const roundedClass = computed(
  () => ({ md: 'rounded-md', lg: 'rounded-lg', xl: 'rounded-xl', full: 'rounded-full' })[props.rounded],
);
</script>

<template>
  <div
    class="space-y-3"
    role="status"
    aria-busy="true"
    aria-label="Loading"
  >
    <div
      v-for="n in count"
      :key="n"
      :class="['animate-pulse bg-slate-200', height, roundedClass]"
    />
    <span class="sr-only">Loading…</span>
  </div>
</template>
