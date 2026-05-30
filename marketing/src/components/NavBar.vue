<script setup lang="ts">
import { ref } from 'vue';
import { Menu, X } from 'lucide-vue-next';
import { REGISTER_URL, DOCS_URL } from '@/constants';

const open = ref(false);
</script>

<template>
  <header class="sticky top-0 z-40 border-b border-slate-100 bg-white/80 backdrop-blur">
    <nav class="mx-auto flex max-w-[1200px] items-center justify-between px-6 py-4">
      <RouterLink
        to="/"
        class="flex items-center gap-2 text-lg font-semibold text-ink"
        aria-label="Flowamaz home"
      >
        <span class="text-primary">✦</span>
        Flowamaz
      </RouterLink>

      <div class="hidden items-center gap-8 md:flex">
        <RouterLink to="/#features" class="text-sm font-medium text-slate-600 hover:text-ink">Product</RouterLink>
        <RouterLink to="/pricing" class="text-sm font-medium text-slate-600 hover:text-ink">Pricing</RouterLink>
        <RouterLink to="/blog" class="text-sm font-medium text-slate-600 hover:text-ink">Blog</RouterLink>
        <a :href="DOCS_URL" class="text-sm font-medium text-slate-600 hover:text-ink">Docs</a>
        <a
          :href="REGISTER_URL"
          class="rounded-lg bg-primary px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-primary-600"
        >Start free trial</a>
      </div>

      <button
        type="button"
        class="text-slate-600 md:hidden"
        :aria-label="open ? 'Close menu' : 'Open menu'"
        @click="open = !open"
      >
        <component :is="open ? X : Menu" class="h-6 w-6" />
      </button>
    </nav>

    <div v-if="open" class="border-t border-slate-100 px-6 py-4 md:hidden">
      <div class="flex flex-col gap-4">
        <RouterLink to="/#features" class="text-sm font-medium text-slate-600" @click="open = false">Product</RouterLink>
        <RouterLink to="/pricing" class="text-sm font-medium text-slate-600" @click="open = false">Pricing</RouterLink>
        <RouterLink to="/blog" class="text-sm font-medium text-slate-600" @click="open = false">Blog</RouterLink>
        <a :href="DOCS_URL" class="text-sm font-medium text-slate-600">Docs</a>
        <a
          :href="REGISTER_URL"
          class="rounded-lg bg-primary px-4 py-2 text-center text-sm font-semibold text-white"
        >Start free trial</a>
      </div>
    </div>
  </header>
</template>
