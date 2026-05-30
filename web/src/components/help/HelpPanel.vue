<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import { X, ChevronDown, ChevronRight, BookOpen, LifeBuoy } from 'lucide-vue-next';
import { useHelp } from '@/composables/useHelp';
import { useWorkspace } from '@/composables/useWorkspace';
import HelpSearch from './HelpSearch.vue';
import ArticleRenderer from './ArticleRenderer.vue';
import HelpFeedbackWidget from './HelpFeedbackWidget.vue';

const help = useHelp();
const ws = useWorkspace();
const collapsed = ref<Record<string, boolean>>({});

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

        <div class="flex-1 overflow-y-auto px-4 py-4">
          <HelpSearch @select="help.selectArticle" />

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
                    @click="help.selectArticle(a.slug)"
                  >
                    {{ a.title }}
                  </button>
                </li>
              </ul>
            </div>
          </nav>

          <div
            v-if="help.currentArticle.value"
            class="mt-6 border-t border-slate-100 pt-5"
          >
            <ArticleRenderer :article="help.currentArticle.value" />
            <HelpFeedbackWidget
              :key="help.currentArticle.value.slug"
              :slug="help.currentArticle.value.slug"
            />
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
