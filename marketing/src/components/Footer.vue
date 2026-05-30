<script setup lang="ts">
import { DOCS_URL, GITHUB_URL } from '@/constants';

const columns = [
  {
    heading: 'Product',
    links: [
      { label: 'Features', to: '/#features' },
      { label: 'Pricing', to: '/pricing' },
      { label: 'Changelog', to: '/changelog' },
      { label: 'Roadmap', to: '/changelog' },
    ],
  },
  {
    heading: 'Company',
    links: [
      { label: 'About', to: '/about' },
      { label: 'Blog', to: '/blog' },
      { label: 'Careers', to: '/about' },
      { label: 'Press', to: '/about' },
    ],
  },
  {
    heading: 'Legal',
    links: [
      { label: 'Privacy Policy', to: '/about' },
      { label: 'Terms of Service', to: '/about' },
      { label: 'Security', to: '/about' },
    ],
  },
];

const social = [
  { label: 'GitHub', href: GITHUB_URL },
  { label: 'Twitter/X', href: 'https://x.com/flowamaz' },
  { label: 'LinkedIn', href: 'https://www.linkedin.com/company/flowamaz' },
  { label: 'Docs', href: DOCS_URL },
];
</script>

<template>
  <footer class="border-t border-slate-100 bg-slate-50">
    <div class="mx-auto max-w-[1200px] px-6 py-14">
      <div class="grid grid-cols-2 gap-8 md:grid-cols-4">
        <div class="col-span-2 md:col-span-1">
          <div class="flex items-center gap-2 text-lg font-semibold text-ink">
            <span class="text-primary">✦</span> Flowamaz
          </div>
          <p class="mt-3 max-w-xs text-sm text-slate-500">
            AI-native workflow orchestration with humans built in.
          </p>
        </div>

        <div v-for="col in columns" :key="col.heading">
          <h3 class="text-xs font-semibold uppercase tracking-wide text-slate-400">{{ col.heading }}</h3>
          <ul class="mt-4 space-y-2">
            <li v-for="link in col.links" :key="link.label">
              <RouterLink :to="link.to" class="text-sm text-slate-600 hover:text-ink">{{ link.label }}</RouterLink>
            </li>
          </ul>
        </div>
      </div>

      <div class="mt-12 flex flex-col items-center justify-between gap-4 border-t border-slate-200 pt-8 sm:flex-row">
        <p class="text-sm text-slate-400">© 2026 Flowamaz. All rights reserved.</p>
        <div class="flex gap-6">
          <a
            v-for="s in social"
            :key="s.label"
            :href="s.href"
            class="text-sm text-slate-500 hover:text-ink"
          >{{ s.label }}</a>
        </div>
      </div>
    </div>
  </footer>
</template>
