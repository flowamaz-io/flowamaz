<script setup lang="ts">
import { computed } from 'vue';
import { Check } from 'lucide-vue-next';
import { registerUrlForPlan, type PlanDef } from '@/constants';

const props = defineProps<{
  plan: PlanDef;
  annual: boolean;
}>();

// Displayed monthly rate: annual billing shows the discounted effective monthly price.
const price = computed(() => (props.annual ? props.plan.annualUsd : props.plan.monthlyUsd));
const isFree = computed(() => price.value === 0);
</script>

<template>
  <div
    :class="[
      'flex flex-col rounded-2xl border bg-white p-8',
      plan.featured ? 'border-primary shadow-lg ring-1 ring-primary-200' : 'border-slate-200 shadow-sm',
    ]"
  >
    <h3 class="text-lg font-semibold text-ink">{{ plan.name }}</h3>
    <p class="mt-1 text-sm text-slate-500">{{ plan.tagline }}</p>

    <div class="mt-6 flex items-baseline gap-1">
      <span class="text-4xl font-bold text-ink" data-test="price">{{ isFree ? '$0' : `$${price}` }}</span>
      <span v-if="!isFree" class="text-sm text-slate-500">/mo{{ annual ? ', billed yearly' : '' }}</span>
    </div>

    <ul class="mt-6 flex-1 space-y-3">
      <li v-for="feature in plan.features" :key="feature" class="flex items-start gap-2 text-sm text-slate-600">
        <Check class="mt-0.5 h-4 w-4 shrink-0 text-primary" />
        <span>{{ feature }}</span>
      </li>
    </ul>

    <a
      :href="registerUrlForPlan(plan.slug)"
      :class="[
        'mt-8 rounded-lg px-4 py-2.5 text-center text-sm font-semibold transition-colors',
        plan.featured
          ? 'bg-primary text-white hover:bg-primary-600'
          : 'border border-slate-200 text-ink hover:bg-slate-50',
      ]"
    >Start free trial</a>
  </div>
</template>
