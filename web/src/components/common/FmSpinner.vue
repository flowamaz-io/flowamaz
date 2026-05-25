<script setup lang="ts">
import { computed } from 'vue';
import { Loader2 } from 'lucide-vue-next';

const props = withDefaults(defineProps<{ size?: 'sm' | 'md' | 'lg' }>(), {
  size: 'md',
});

const sizeClass = computed(
  () => ({ sm: 'h-4 w-4', md: 'h-5 w-5', lg: 'h-8 w-8' })[props.size],
);
</script>

<template>
  <Loader2
    :class="['animate-spin', sizeClass]"
    aria-hidden="true"
  />
</template>
