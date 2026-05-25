<script setup lang="ts">
import { computed } from 'vue';
import type { WorkspaceRole } from '@/types';
import { ROLE_BADGE } from '@/utils/constants';

const props = withDefaults(
  defineProps<{
    /** Render as a role badge with the canonical role colour. */
    role?: WorkspaceRole;
    /** Render as a status badge. */
    status?: 'active' | 'inactive';
    /** Free-form colour variant when neither role nor status applies. */
    variant?: 'primary' | 'accent' | 'amber' | 'danger' | 'slate' | 'blue';
  }>(),
  { variant: 'slate' },
);

const cls = computed(() => {
  if (props.role) return ROLE_BADGE[props.role];
  if (props.status) return props.status === 'active' ? 'bg-primary-100 text-primary-700' : 'bg-slate-100 text-slate-500';
  return {
    primary: 'bg-primary-100 text-primary-700',
    accent: 'bg-accent-100 text-accent-700',
    amber: 'bg-amber-100 text-amber-700',
    danger: 'bg-danger-100 text-danger-700',
    slate: 'bg-slate-100 text-slate-600',
    blue: 'bg-blue-100 text-blue-700',
  }[props.variant];
});
</script>

<template>
  <span :class="['inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium', cls]">
    <slot>{{ role ?? status }}</slot>
  </span>
</template>
