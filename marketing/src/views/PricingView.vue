<script setup lang="ts">
import { ref } from 'vue';
import PricingCard from '@/components/PricingCard.vue';
import { PLANS } from '@/constants';

const annual = ref(false);
</script>

<template>
  <section class="mx-auto max-w-[1200px] px-6 py-20">
    <div class="text-center">
      <h1 class="text-4xl font-bold text-ink">Pricing that scales with you</h1>
      <p class="mx-auto mt-4 max-w-xl text-lg text-slate-600">
        Start free. Upgrade when your automation does more. No surprises.
      </p>
    </div>

    <!-- Monthly / annual toggle -->
    <div class="mt-10 flex items-center justify-center gap-4">
      <span :class="['text-sm font-medium', annual ? 'text-slate-400' : 'text-ink']">Monthly</span>
      <button
        type="button"
        role="switch"
        :aria-checked="annual"
        aria-label="Toggle annual billing"
        class="relative h-6 w-11 rounded-full bg-primary transition-colors"
        @click="annual = !annual"
      >
        <span
          :class="[
            'absolute top-0.5 h-5 w-5 rounded-full bg-white transition-transform',
            annual ? 'translate-x-5' : 'translate-x-0.5',
          ]"
        />
      </button>
      <span :class="['text-sm font-medium', annual ? 'text-ink' : 'text-slate-400']">
        Annual <span class="text-primary">(save ~20%)</span>
      </span>
    </div>

    <div class="mt-12 grid gap-8 md:grid-cols-3">
      <PricingCard v-for="plan in PLANS" :key="plan.slug" :plan="plan" :annual="annual" />
    </div>
  </section>
</template>
