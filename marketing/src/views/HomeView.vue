<script setup lang="ts">
import HeroSection from '@/components/HeroSection.vue';
import FeatureSection from '@/components/FeatureSection.vue';
import TestimonialSection from '@/components/TestimonialSection.vue';
import PricingCard from '@/components/PricingCard.vue';
import { PLANS } from '@/constants';

const proofLogos = ['Northwind', 'Acme', 'Globex', 'Initech'];

const steps = [
  { n: 1, title: 'Describe', body: 'Write what you want to automate in plain English.' },
  { n: 2, title: 'Review on canvas', body: 'See the generated workflow and adjust any step.' },
  { n: 3, title: 'Run and monitor', body: 'Trigger it and watch every run in real time.' },
];
</script>

<template>
  <div>
    <HeroSection />

    <!-- Social proof strip -->
    <section class="mx-auto max-w-[1200px] px-6 pb-8">
      <p class="text-center text-sm font-medium uppercase tracking-wide text-slate-400">
        Trusted by teams at
      </p>
      <div class="mt-6 flex flex-wrap items-center justify-center gap-x-12 gap-y-4">
        <span v-for="logo in proofLogos" :key="logo" class="text-lg font-semibold text-slate-300">{{ logo }}</span>
      </div>
    </section>

    <FeatureSection />

    <!-- How it works -->
    <section id="how-it-works" class="bg-slate-50 py-20">
      <div class="mx-auto max-w-[1200px] px-6">
        <h2 class="text-center text-3xl font-bold text-ink">How it works</h2>
        <div class="mt-12 grid gap-8 md:grid-cols-3">
          <div v-for="step in steps" :key="step.n" class="relative text-center">
            <div class="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-primary text-lg font-bold text-white">
              {{ step.n }}
            </div>
            <h3 class="mt-5 text-xl font-semibold text-ink">{{ step.title }}</h3>
            <p class="mt-2 text-slate-600">{{ step.body }}</p>
            <div
              v-if="step.n < 3"
              class="absolute right-0 top-6 hidden text-2xl text-primary-300 md:block"
              aria-hidden="true"
            >→</div>
          </div>
        </div>
      </div>
    </section>

    <!-- Workflow Weather -->
    <section class="mx-auto max-w-[1200px] px-6 py-20">
      <div class="grid items-center gap-10 md:grid-cols-2">
        <div>
          <h2 class="text-3xl font-bold text-ink">Know your process health at a glance</h2>
          <p class="mt-4 text-lg text-slate-600">
            Workflow Weather scores every workflow on reliability, cost, and speed — so you spot
            trouble before it reaches your customers.
          </p>
        </div>
        <div class="rounded-2xl border border-slate-100 bg-slate-50 p-8">
          <div class="flex items-center gap-4">
            <span class="text-5xl" aria-hidden="true">⛅</span>
            <div>
              <p class="text-sm font-medium uppercase tracking-wide text-slate-400">Order fulfilment</p>
              <p class="text-2xl font-bold text-ink">Mostly sunny · 92</p>
            </div>
          </div>
          <div class="mt-6 grid grid-cols-3 gap-4 text-center">
            <div class="rounded-lg bg-white p-3">
              <p class="text-xs text-slate-400">Reliability</p>
              <p class="text-lg font-semibold text-primary">99.1%</p>
            </div>
            <div class="rounded-lg bg-white p-3">
              <p class="text-xs text-slate-400">Avg cost</p>
              <p class="text-lg font-semibold text-ink">$0.012</p>
            </div>
            <div class="rounded-lg bg-white p-3">
              <p class="text-xs text-slate-400">P95 time</p>
              <p class="text-lg font-semibold text-ink">1.4s</p>
            </div>
          </div>
        </div>
      </div>
    </section>

    <TestimonialSection />

    <!-- Pricing teaser -->
    <section class="mx-auto max-w-[1200px] px-6 py-20">
      <h2 class="text-center text-3xl font-bold text-ink">Simple, transparent pricing</h2>
      <div class="mt-12 grid gap-8 md:grid-cols-3">
        <PricingCard v-for="plan in PLANS" :key="plan.slug" :plan="plan" :annual="false" />
      </div>
      <div class="mt-10 text-center">
        <RouterLink to="/pricing" class="text-sm font-semibold text-primary hover:text-primary-600">
          See full pricing →
        </RouterLink>
      </div>
    </section>
  </div>
</template>
