<script setup lang="ts">
import { computed, ref } from 'vue';
import { refDebounced } from '@vueuse/core';
import { Search } from 'lucide-vue-next';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import { allArticles, type HelpArticle } from '@/help/articleLoader';
import { escapeHtml } from '@/utils/string.util';

const emit = defineEmits<{ select: [slug: string] }>();

const query = ref('');
const debounced = refDebounced(query, 300);

interface SearchHit {
  article: HelpArticle;
  snippet: string;
}

function buildSnippet(content: string, term: string): string {
  const lower = content.toLowerCase();
  const idx = lower.indexOf(term.toLowerCase());
  const start = idx >= 0 ? Math.max(0, idx - 30) : 0;
  const raw = content.slice(start, start + 120).replace(/\n+/g, ' ').trim();
  const escaped = escapeHtml(raw);
  if (!term) return escaped;
  const re = new RegExp(`(${term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`, 'ig');
  return escaped.replace(re, '<mark>$1</mark>');
}

const results = computed<SearchHit[]>(() => {
  const term = debounced.value.trim().toLowerCase();
  if (term.length === 0) return [];
  return allArticles()
    .filter((a) => a.title.toLowerCase().includes(term) || a.content.toLowerCase().includes(term))
    .map((a) => ({ article: a, snippet: buildSnippet(a.content, debounced.value.trim()) }));
});
</script>

<template>
  <div>
    <div class="relative">
      <Search class="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
      <input
        v-model="query"
        type="search"
        placeholder="Search help articles…"
        class="w-full rounded-lg border border-slate-300 bg-white py-2 pl-9 pr-3 text-sm focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
      >
    </div>

    <ul
      v-if="results.length > 0"
      class="mt-3 space-y-1"
    >
      <li
        v-for="hit in results"
        :key="hit.article.slug"
      >
        <button
          type="button"
          class="w-full rounded-lg px-3 py-2 text-left hover:bg-slate-100"
          @click="emit('select', hit.article.slug)"
        >
          <p class="text-sm font-medium text-slate-800">
            {{ hit.article.title }}
          </p>
          <p class="text-xs text-slate-400">
            {{ hit.article.section }}
          </p>
          <p
            class="mt-1 text-xs text-slate-500"
            v-html="hit.snippet"
          />
        </button>
      </li>
    </ul>

    <FmEmptyState
      v-else-if="debounced.trim().length > 0"
      title="No matching articles"
      description="Try different keywords, or browse the navigation tree below. Full docs live at docs.flowamaz.io."
      class="mt-4"
    />
  </div>
</template>
