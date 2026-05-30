<script setup lang="ts">
const features = [
  {
    icon: '✦',
    title: 'Plain English creation',
    body: 'Describe your process. Flowamaz builds the workflow.',
  },
  {
    icon: '👤',
    title: 'Human in the loop',
    body: 'Approvals, reviews, and decisions built natively into every workflow.',
  },
  {
    icon: '🔌',
    title: 'Connect everything',
    body: '13 official connectors. Any API via HTTP/REST.',
  },
];
</script>

<template>
  <section id="features" class="mx-auto max-w-[1200px] px-6 py-20">
    <div class="grid gap-10 md:grid-cols-3">
      <div
        v-for="f in features"
        :key="f.title"
        class="rounded-2xl border border-slate-100 bg-white p-8 shadow-sm"
      >
        <div class="text-3xl">{{ f.icon }}</div>
        <h3 class="mt-5 text-xl font-semibold text-ink">{{ f.title }}</h3>
        <p class="mt-3 text-slate-600">{{ f.body }}</p>
      </div>
    </div>
  </section>
</template>
