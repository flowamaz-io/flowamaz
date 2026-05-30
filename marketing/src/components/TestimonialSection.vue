<script setup lang="ts">
const testimonials = [
  {
    quote: 'We replaced three brittle Zapier chains with one Flowamaz workflow — and finally got approvals in the right place.',
    name: 'Operations Lead',
    company: 'Placeholder Co.',
  },
  {
    quote: 'Describing a process in a sentence and watching it become a real, monitored workflow still feels like magic.',
    name: 'Engineering Manager',
    company: 'Placeholder Inc.',
  },
];
</script>

<template>
  <section class="bg-slate-50 py-20">
    <div class="mx-auto max-w-[1200px] px-6">
      <div class="grid gap-8 md:grid-cols-2">
        <figure
          v-for="t in testimonials"
          :key="t.name"
          class="rounded-2xl border border-slate-100 bg-white p-8"
        >
          <blockquote class="text-lg text-ink-soft">“{{ t.quote }}”</blockquote>
          <figcaption class="mt-6 text-sm font-medium text-slate-500">
            {{ t.name }} · {{ t.company }}
          </figcaption>
        </figure>
      </div>
    </div>
  </section>
</template>
