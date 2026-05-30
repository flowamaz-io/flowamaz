<script setup lang="ts">
import { REGISTER_URL } from '@/constants';
</script>

<template>
  <section class="mx-auto max-w-[1200px] px-6 pb-16 pt-20 text-center">
    <h1 class="mx-auto max-w-3xl text-4xl font-bold tracking-tight text-ink sm:text-6xl">
      Automate anything. In plain English.
    </h1>
    <p class="mx-auto mt-6 max-w-2xl text-lg text-slate-600">
      Flowamaz turns your process descriptions into running workflows — with human approvals,
      AI decisions, and real-time monitoring built in.
    </p>

    <div class="mt-10 flex flex-col items-center justify-center gap-4 sm:flex-row">
      <a
        :href="REGISTER_URL"
        class="rounded-lg bg-primary px-6 py-3 text-base font-semibold text-white transition-colors hover:bg-primary-600"
      >Start your free trial</a>
      <a
        href="#how-it-works"
        class="rounded-lg border border-slate-200 px-6 py-3 text-base font-semibold text-ink transition-colors hover:bg-slate-50"
      >See how it works</a>
    </div>

    <!-- Hero visual: animated SVG workflow diagram, teal nodes connecting left → right. -->
    <div class="mx-auto mt-16 max-w-3xl">
      <svg viewBox="0 0 600 180" class="w-full" role="img" aria-label="Animated workflow diagram">
        <line x1="110" y1="90" x2="250" y2="50" stroke="#a7e5cb" stroke-width="3" />
        <line x1="110" y1="90" x2="250" y2="130" stroke="#a7e5cb" stroke-width="3" />
        <line x1="310" y1="50" x2="450" y2="90" stroke="#a7e5cb" stroke-width="3" />
        <line x1="310" y1="130" x2="450" y2="90" stroke="#a7e5cb" stroke-width="3" />

        <g class="animate-pulse">
          <circle cx="80" cy="90" r="30" fill="#1d9e75" />
          <circle cx="280" cy="50" r="30" fill="#38b889" />
          <circle cx="280" cy="130" r="30" fill="#38b889" />
          <circle cx="480" cy="90" r="30" fill="#137e5e" />
        </g>
        <text x="80" y="95" text-anchor="middle" fill="white" font-size="13" font-weight="600">Start</text>
        <text x="280" y="55" text-anchor="middle" fill="white" font-size="12" font-weight="600">AI</text>
        <text x="280" y="135" text-anchor="middle" fill="white" font-size="11" font-weight="600">Gate</text>
        <text x="480" y="95" text-anchor="middle" fill="white" font-size="12" font-weight="600">Done</text>
      </svg>
    </div>
  </section>
</template>
