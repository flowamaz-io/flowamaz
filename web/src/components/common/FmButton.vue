<script setup lang="ts">
import { computed } from 'vue';
import FmSpinner from './FmSpinner.vue';

const props = withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary' | 'danger' | 'ghost';
    size?: 'sm' | 'md' | 'lg';
    type?: 'button' | 'submit' | 'reset';
    loading?: boolean;
    disabled?: boolean;
    block?: boolean;
  }>(),
  {
    variant: 'primary',
    size: 'md',
    type: 'button',
    loading: false,
    disabled: false,
    block: false,
  },
);

const variantClass = computed(
  () =>
    ({
      primary: 'bg-primary-600 text-white hover:bg-primary-700 focus-visible:ring-primary-500',
      secondary:
        'bg-white text-slate-700 border border-slate-300 hover:bg-slate-50 focus-visible:ring-slate-400',
      danger: 'bg-danger-600 text-white hover:bg-danger-700 focus-visible:ring-danger-500',
      ghost: 'bg-transparent text-slate-600 hover:bg-slate-100 focus-visible:ring-slate-400',
    })[props.variant],
);

const sizeClass = computed(
  () =>
    ({
      sm: 'px-2.5 py-1.5 text-sm gap-1.5',
      md: 'px-4 py-2 text-sm gap-2',
      lg: 'px-5 py-2.5 text-base gap-2',
    })[props.size],
);

const isDisabled = computed(() => props.disabled || props.loading);
</script>

<template>
  <button
    :type="type"
    :disabled="isDisabled"
    :class="[
      'inline-flex items-center justify-center rounded-lg font-medium transition-colors',
      'focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-2',
      'disabled:cursor-not-allowed disabled:opacity-60',
      variantClass,
      sizeClass,
      block ? 'w-full' : '',
    ]"
  >
    <FmSpinner
      v-if="loading"
      size="sm"
    />
    <slot
      v-else
      name="icon-left"
    />
    <span v-if="!loading || $slots.default"><slot /></span>
    <slot
      v-if="!loading"
      name="icon-right"
    />
  </button>
</template>
