<script setup lang="ts">
import { computed } from 'vue';
import { formatPrice, isPaid } from '@/utils/money.util';
import type { PricingModel } from '@/types/marketplace';

const props = withDefaults(
  defineProps<{
    pricingModel?: PricingModel;
    priceCents?: number;
    currency?: string;
    size?: 'sm' | 'md';
  }>(),
  { pricingModel: 'free', priceCents: 0, currency: 'usd', size: 'sm' },
);

const paid = computed(() => isPaid(props.pricingModel, props.priceCents));
const label = computed(() => formatPrice(props.pricingModel, props.priceCents, props.currency));
</script>

<template>
  <span
    :class="[
      'inline-flex shrink-0 items-center rounded-full font-semibold',
      size === 'md' ? 'px-2.5 py-1 text-sm' : 'px-2 py-0.5 text-xs',
      paid ? 'bg-amber-100 text-amber-800' : 'bg-slate-100 text-slate-600',
    ]"
    data-test="price-badge"
  >
    {{ label }}
  </span>
</template>
