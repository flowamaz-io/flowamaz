<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, watch } from 'vue';
import { useRoute } from 'vue-router';
import { Menu } from 'lucide-vue-next';
import Sidebar from './Sidebar.vue';
import HelpPanel from '@/components/help/HelpPanel.vue';
import { useUiStore } from '@/stores/ui.store';
import { useHelp } from '@/composables/useHelp';
import { useWorkspace } from '@/composables/useWorkspace';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { articleForRoute } from '@/help/articleMap';

const ui = useUiStore();
const help = useHelp();
const ws = useWorkspace();
const route = useRoute();
const toast = useToast();

const isFullScreen = computed(() =>
  route.name === 'workflow-editor' ||
  (route.name === 'workflow-new' && route.query.method === 'canvas'),
);

// Shift+? opens the help panel globally (FUNCTIONAL.md §10.1). "?" is Shift+/ on most layouts.
// [ toggles the sidebar expand/collapse on desktop.
function onKeydown(event: KeyboardEvent): void {
  const target = event.target as HTMLElement | null;
  const typing = target && ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName);
  if (event.shiftKey && event.key === '?' && !typing) {
    event.preventDefault();
    help.openForRoute(route.path);
  }
  if (event.key === '[' && !event.metaKey && !event.ctrlKey && !typing) {
    ui.toggleSidebarMinimized();
  }
}

// Collapse sidebar by default on small screens.
function syncSidebarToViewport(): void {
  ui.setSidebar(window.innerWidth < 768);
}

onMounted(async () => {
  document.addEventListener('keydown', onKeydown);
  window.addEventListener('resize', syncSidebarToViewport);
  syncSidebarToViewport();
  try {
    await ws.loadWorkspaces();
  } catch (error) {
    toast.error(toUserFacingError(error).message);
  }
});

onBeforeUnmount(() => {
  document.removeEventListener('keydown', onKeydown);
  window.removeEventListener('resize', syncSidebarToViewport);
});

// Keep the help-panel default article in sync with the current route.
watch(
  () => route.path,
  (path) => ui.setHelpArticle(articleForRoute(path)),
  { immediate: true },
);
</script>

<template>
  <div class="flex h-full">
    <Sidebar />
    <!-- The Sidebar is md:static so on desktop it occupies 220px / 52px of flow width
         (collapsed) — content fills the remainder with no margin needed and no layout jump.
         On mobile the Sidebar is a fixed slide-in overlay, so content is full-width. -->
    <div class="flex min-w-0 flex-1 flex-col transition-all duration-200 ease-in-out">
      <!-- Mobile hamburger — opens the slide-in sidebar overlay. -->
      <button
        class="absolute left-3 top-3 z-20 rounded-lg bg-sidebar-bg p-2 text-white shadow-lg md:hidden"
        aria-label="Open menu"
        @click="ui.setSidebar(false)"
      >
        <Menu class="h-5 w-5" />
      </button>
      <main :class="isFullScreen ? 'flex-1 overflow-hidden flex flex-col' : 'flex-1 overflow-y-auto bg-slate-50'">
        <RouterView />
      </main>
    </div>
    <HelpPanel />
  </div>
</template>
