<script setup lang="ts">
import type { Component } from 'vue';
import FmButton from './FmButton.vue';

// FmEmptyState NEVER renders a bare "No results" — every empty state teaches the next step
// (CLAUDE.md rule 9). `description` + a CTA are the contract.
defineProps<{
  icon?: Component;
  title: string;
  description: string;
  ctaLabel?: string;
}>();

defineEmits<{ cta: [] }>();
</script>

<template>
  <div class="flex flex-col items-center justify-center rounded-xl border border-dashed border-slate-300 bg-white px-6 py-12 text-center">
    <div
      v-if="icon"
      class="mb-4 flex h-12 w-12 items-center justify-center rounded-full bg-primary-50 text-primary-600"
    >
      <component
        :is="icon"
        class="h-6 w-6"
      />
    </div>
    <h3 class="text-base font-semibold text-slate-900">
      {{ title }}
    </h3>
    <p class="mt-1 max-w-sm text-sm text-slate-500">
      {{ description }}
    </p>
    <FmButton
      v-if="ctaLabel"
      class="mt-5"
      size="md"
      @click="$emit('cta')"
    >
      {{ ctaLabel }}
    </FmButton>
    <slot name="action" />
  </div>
</template>
