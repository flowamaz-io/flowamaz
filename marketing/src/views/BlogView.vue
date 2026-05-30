<script setup lang="ts">
// Static placeholder posts. A real CMS/markdown pipeline arrives post-launch.
const posts = [
  {
    title: 'Why we built Flowamaz',
    excerpt: 'The founder story — and why workflow tools keep forgetting about the humans in the loop.',
    date: 'May 2026',
    tag: 'Story',
  },
  {
    title: 'Human gates: the missing piece in workflow automation',
    excerpt: 'Approvals are not an afterthought. Here is how native human gates change how teams automate.',
    date: 'May 2026',
    tag: 'Product',
  },
  {
    title: 'From plain English to running workflow in 30 seconds',
    excerpt: 'A walkthrough: describe a process, review the canvas, and ship it — start to finish.',
    date: 'April 2026',
    tag: 'Tutorial',
  },
];
</script>

<template>
  <div class="mx-auto max-w-[1200px] px-6 py-20">
    <h1 class="text-4xl font-bold text-ink">Blog</h1>
    <p class="mt-3 text-lg text-slate-600">Notes on automation, humans in the loop, and building Flowamaz.</p>

    <div class="mt-12 grid gap-8 md:grid-cols-3">
      <article
        v-for="post in posts"
        :key="post.title"
        class="flex flex-col rounded-2xl border border-slate-100 bg-white p-8 shadow-sm"
      >
        <span class="self-start rounded-full bg-primary-50 px-3 py-1 text-xs font-semibold text-primary-700">
          {{ post.tag }}
        </span>
        <h2 class="mt-5 text-xl font-semibold text-ink">{{ post.title }}</h2>
        <p class="mt-3 flex-1 text-slate-600">{{ post.excerpt }}</p>
        <p class="mt-6 text-sm text-slate-400">{{ post.date }}</p>
      </article>
    </div>
  </div>
</template>
