<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { X, ChevronDown, ChevronRight, BookOpen, LifeBuoy, ArrowLeft } from 'lucide-vue-next';
import { useHelp } from '@/composables/useHelp';
import { useWorkspace } from '@/composables/useWorkspace';
import HelpSearch from './HelpSearch.vue';
import ArticleRenderer from './ArticleRenderer.vue';
import HelpFeedbackWidget from './HelpFeedbackWidget.vue';

const help = useHelp();
const ws = useWorkspace();
const collapsed = ref<Record<string, boolean>>({});
/** True while the user is browsing search + the article tree instead of reading an article. */
const browsing = ref(false);
const scroller = ref<HTMLElement | null>(null);
const articleEl = ref<HTMLElement | null>(null);

const showArticle = computed(() => !!help.currentArticle.value && !browsing.value);

/** Swap the list for the article and move focus to it, so the selection is visible immediately. */
async function revealArticle(): Promise<void> {
  browsing.value = false;
  await nextTick();
  scroller.value?.scrollTo({ top: 0 });
  articleEl.value?.focus({ preventScroll: true });
}

function openArticle(slug: string): void {
  help.selectArticle(slug);
  void revealArticle();
}

async function backToBrowse(): Promise<void> {
  browsing.value = true;
  await nextTick();
  scroller.value?.scrollTo({ top: 0 });
}

// Every open request decides the view: contextual help (openForRoute / openArticle) opens
// straight on its article; the generic Help button opens on search + the article list. The route
// keeps `currentSlug` up to date in the background, which only highlights it in the list.
watch(
  [() => help.isOpen.value, () => help.requestSeq.value],
  ([open]) => {
    if (open) browsing.value = help.view.value === 'browse';
  },
  { immediate: true },
);

const supportMailto = computed(() => {
  const page = window.location.pathname;
  const org = ws.current.value?.name ?? 'my organisation';
  const subject = encodeURIComponent(`Flowamaz Support — ${page} — ${org}`);
  return `mailto:support@flowamaz.io?subject=${subject}`;
});

function toggleSection(section: string): void {
  collapsed.value[section] = !collapsed.value[section];
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && help.isOpen.value) help.close();
}

onMounted(() => document.addEventListener('keydown', onKeydown));
onBeforeUnmount(() => document.removeEventListener('keydown', onKeydown));
</script>

<template>
  <Teleport to="body">
    <Transition
      enter-active-class="transition duration-200 ease-out"
      enter-from-class="translate-x-full"
      leave-active-class="transition duration-150 ease-in"
      leave-to-class="translate-x-full"
    >
      <aside
        v-if="help.isOpen.value"
        class="fixed inset-y-0 right-0 z-50 flex w-[340px] max-w-full flex-col border-l border-slate-200 bg-white shadow-2xl"
        aria-label="Help panel"
      >
        <header class="flex items-center justify-between border-b border-slate-200 px-4 py-3">
          <div class="flex items-center gap-2 text-slate-900">
            <BookOpen class="h-5 w-5 text-primary-600" />
            <span class="font-semibold">Help</span>
          </div>
          <button
            type="button"
            class="rounded-lg p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600"
            aria-label="Close help"
            @click="help.close()"
          >
            <X class="h-5 w-5" />
          </button>
        </header>

        <div
          ref="scroller"
          class="flex-1 overflow-y-auto px-4 py-4"
        >
          <!-- Article view replaces the list (kept mounted with v-show so the search query survives Back). -->
          <div
            v-if="showArticle && help.currentArticle.value"
            ref="articleEl"
            tabindex="-1"
            class="focus:outline-none"
            data-testid="help-article-view"
          >
            <button
              type="button"
              class="mb-4 inline-flex items-center gap-1 text-sm font-medium text-primary-700 hover:text-primary-800"
              data-testid="help-back"
              @click="backToBrowse"
            >
              <ArrowLeft class="h-4 w-4" />
              All help articles
            </button>
            <ArticleRenderer :article="help.currentArticle.value" />
            <HelpFeedbackWidget
              :key="help.currentArticle.value.slug"
              :slug="help.currentArticle.value.slug"
            />
          </div>

          <div v-show="!showArticle">
            <HelpSearch @select="openArticle" />

            <nav class="mt-5 border-t border-slate-100 pt-4">
              <div
                v-for="group in help.tree"
                :key="group.section"
                class="mb-2"
              >
                <button
                  type="button"
                  class="flex w-full items-center gap-1 rounded px-1 py-1 text-left text-xs font-semibold uppercase tracking-wide text-slate-500 hover:text-slate-700"
                  @click="toggleSection(group.section)"
                >
                  <component
                    :is="collapsed[group.section] ? ChevronRight : ChevronDown"
                    class="h-3.5 w-3.5"
                  />
                  {{ group.section }}
                </button>
                <ul
                  v-show="!collapsed[group.section]"
                  class="ml-3 mt-1 space-y-0.5"
                >
                  <li
                    v-for="a in group.articles"
                    :key="a.slug"
                  >
                    <button
                      type="button"
                      :class="[
                        'w-full rounded-md px-2 py-1 text-left text-sm hover:bg-slate-100',
                        help.currentSlug.value === a.slug ? 'bg-primary-50 font-medium text-primary-700' : 'text-slate-600',
                      ]"
                      @click="openArticle(a.slug)"
                    >
                      {{ a.title }}
                    </button>
                  </li>
                </ul>
              </div>
            </nav>
          </div>
        </div>

        <footer class="border-t border-slate-200 px-4 py-3">
          <a
            :href="supportMailto"
            class="flex items-center justify-center gap-2 rounded-lg border border-slate-200 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50"
          >
            <LifeBuoy class="h-4 w-4" />
            Contact support
          </a>
        </footer>
      </aside>
    </Transition>
  </Teleport>
</template>
