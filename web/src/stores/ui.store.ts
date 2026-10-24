import { defineStore } from 'pinia';
import { ref } from 'vue';
import type { Toast } from '@/types';

let toastSeq = 0;

const SIDEBAR_MINIMIZED_KEY = 'fmz_sidebar_collapsed';

/** Global UI state: help panel, sidebar, and the toast queue. */
export const useUiStore = defineStore('ui', () => {
  const helpPanelOpen = ref(false);
  const helpPanelArticle = ref<string | null>(null);
  /**
   * What the help panel shows when it is (re)opened: the search + article list ('browse'), or the
   * article it was opened for ('article'). `helpRequestSeq` bumps on every open request so the
   * panel re-applies it even when it is already open.
   */
  const helpPanelView = ref<'browse' | 'article'>('browse');
  const helpRequestSeq = ref(0);
  const sidebarCollapsed = ref(false);
  const sidebarMinimized = ref(localStorage.getItem(SIDEBAR_MINIMIZED_KEY) === 'true');
  const toasts = ref<Toast[]>([]);

  /** With an article: open straight on it. Without: open on search + the article list. */
  function openHelp(article?: string): void {
    if (article) helpPanelArticle.value = article;
    helpPanelView.value = article ? 'article' : 'browse';
    helpRequestSeq.value++;
    helpPanelOpen.value = true;
  }

  function closeHelp(): void {
    helpPanelOpen.value = false;
  }

  function setHelpArticle(article: string): void {
    helpPanelArticle.value = article;
  }

  function toggleSidebar(): void {
    sidebarCollapsed.value = !sidebarCollapsed.value;
  }

  function setSidebar(collapsed: boolean): void {
    sidebarCollapsed.value = collapsed;
  }

  function toggleSidebarMinimized(): void {
    sidebarMinimized.value = !sidebarMinimized.value;
    localStorage.setItem(SIDEBAR_MINIMIZED_KEY, String(sidebarMinimized.value));
  }

  function dismissToast(id: number): void {
    toasts.value = toasts.value.filter((t) => t.id !== id);
  }

  function addToast(type: Toast['type'], message: string): void {
    const id = ++toastSeq;
    // Max 3 visible at once — drop the oldest.
    if (toasts.value.length >= 3) toasts.value.shift();
    toasts.value.push({ id, type, message });
    window.setTimeout(() => dismissToast(id), 5000);
  }

  return {
    helpPanelOpen,
    helpPanelArticle,
    helpPanelView,
    helpRequestSeq,
    sidebarCollapsed,
    sidebarMinimized,
    toasts,
    openHelp,
    closeHelp,
    setHelpArticle,
    toggleSidebar,
    setSidebar,
    toggleSidebarMinimized,
    addToast,
    dismissToast,
  };
});
