<script setup lang="ts">
import { REGISTER_URL } from '@/constants';

const values = [
  { title: 'Transparency', body: 'Open pricing, open source core, and an audit trail on everything.' },
  { title: 'Human-first', body: 'People approve, decide, and stay in control — automation serves them.' },
  { title: 'Developer-friendly', body: 'Git-native versioning, a real API, and a CLI that respects your time.' },
];
</script>

<template>
  <div class="mx-auto max-w-[1200px] px-6 py-20">
    <section class="mx-auto max-w-3xl text-center">
      <h1 class="text-4xl font-bold text-ink">Our mission</h1>
      <p class="mt-6 text-xl text-slate-600">
        We're building the workflow automation platform that treats humans as first-class
        participants, not afterthoughts.
      </p>
    </section>

    <section class="mt-20 grid gap-8 md:grid-cols-3">
      <div v-for="value in values" :key="value.title" class="rounded-2xl border border-slate-100 bg-white p-8 shadow-sm">
        <h3 class="text-xl font-semibold text-ink">{{ value.title }}</h3>
        <p class="mt-3 text-slate-600">{{ value.body }}</p>
      </div>
    </section>

    <section class="mt-20 rounded-2xl bg-slate-50 px-6 py-14 text-center">
      <h2 class="text-2xl font-bold text-ink">The team</h2>
      <p class="mx-auto mt-3 max-w-xl text-slate-600">
        We're a small, senior team that ships. We're hiring engineers and designers who care about
        the craft of automation.
      </p>
      <a
        :href="REGISTER_URL"
        class="mt-8 inline-block rounded-lg bg-primary px-6 py-3 text-sm font-semibold text-white hover:bg-primary-600"
      >We're hiring — start building</a>
    </section>
  </div>
</template>
