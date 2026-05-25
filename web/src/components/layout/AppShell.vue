<script setup lang="ts">
import { onBeforeUnmount, onMounted, watch } from 'vue';
import { useRoute } from 'vue-router';
import Sidebar from './Sidebar.vue';
import TopBar from './TopBar.vue';
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

// Shift+? opens the help panel globally (FUNCTIONAL.md §10.1). "?" is Shift+/ on most layouts.
function onKeydown(event: KeyboardEvent): void {
  const target = event.target as HTMLElement | null;
  const typing = target && ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName);
  if (event.shiftKey && event.key === '?' && !typing) {
    event.preventDefault();
    help.openForRoute(route.path);
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
    <div class="flex min-w-0 flex-1 flex-col">
      <TopBar />
      <main class="flex-1 overflow-y-auto bg-slate-50">
        <RouterView />
      </main>
    </div>
    <HelpPanel />
  </div>
</template>
