<script setup lang="ts">
import { computed, ref } from 'vue';
import { marked } from 'marked';
import { ThumbsUp, ThumbsDown, ExternalLink } from 'lucide-vue-next';
import { DOCS_GITHUB_BASE } from '@/utils/constants';
import { useToast } from '@/composables/useToast';
import type { HelpArticle } from '@/help/articleLoader';

const props = defineProps<{ article: HelpArticle }>();

const toast = useToast();
const feedback = ref<'up' | 'down' | null>(null);

const html = computed(() => marked.parse(props.article.content, { async: false }) as string);
const editUrl = computed(() => `${DOCS_GITHUB_BASE}/${props.article.slug}.md`);

// Plan badge colour by tier (FUNCTIONAL.md §1.6 editions).
const planBadgeClass = computed(() => {
  switch (props.article.plans) {
    case 'Cloud only':
      return 'bg-primary-50 text-primary-700';
    case 'Enterprise':
      return 'bg-amber-50 text-amber-700';
    default:
      return 'bg-slate-100 text-slate-600';
  }
});

function rate(value: 'up' | 'down'): void {
  feedback.value = value;
  toast.success(value === 'up' ? 'Thanks for the feedback!' : 'Thanks — we will improve this article.');
}
</script>

<template>
  <article>
    <div
      v-if="article.plans || article.readingTime"
      class="mb-4 flex items-center gap-2 text-xs"
    >
      <span
        v-if="article.plans"
        :class="['inline-flex items-center rounded-full px-2 py-0.5 font-medium', planBadgeClass]"
      >
        {{ article.plans }}
      </span>
      <span
        v-if="article.readingTime"
        class="text-slate-400"
      >
        {{ article.readingTime }}
      </span>
    </div>
    <!-- Content is bundled, trusted markdown (our own docs) — safe to render. -->
    <div
      class="fmz-prose"
      v-html="html"
    />
    <footer class="mt-8 border-t border-slate-200 pt-4">
      <div class="flex items-center justify-between text-sm text-slate-500">
        <div class="flex items-center gap-2">
          <span>Was this helpful?</span>
          <button
            type="button"
            :class="['rounded-md p-1 hover:bg-slate-100', feedback === 'up' ? 'text-primary-600' : '']"
            aria-label="Yes, helpful"
            @click="rate('up')"
          >
            <ThumbsUp class="h-4 w-4" />
          </button>
          <button
            type="button"
            :class="['rounded-md p-1 hover:bg-slate-100', feedback === 'down' ? 'text-danger-600' : '']"
            aria-label="No, not helpful"
            @click="rate('down')"
          >
            <ThumbsDown class="h-4 w-4" />
          </button>
        </div>
        <a
          :href="editUrl"
          target="_blank"
          rel="noopener noreferrer"
          class="inline-flex items-center gap-1 text-primary-600 hover:text-primary-700"
        >
          Edit on GitHub
          <ExternalLink class="h-3.5 w-3.5" />
        </a>
      </div>
    </footer>
  </article>
</template>
