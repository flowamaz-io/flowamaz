import { computed } from 'vue';
import { useUiStore } from '@/stores/ui.store';
import { getArticle, helpTree } from '@/help/articleLoader';
import { articleForRoute } from '@/help/articleMap';

/** Help-panel orchestration: open/close, current article, and the navigation tree. */
export function useHelp() {
  const ui = useUiStore();

  const currentArticle = computed(() =>
    ui.helpPanelArticle ? getArticle(ui.helpPanelArticle) : null,
  );

  function openForRoute(path: string): void {
    ui.openHelp(articleForRoute(path));
  }

  function openArticle(slug: string): void {
    ui.openHelp(slug);
  }

  /** Open the panel on search + the article list (the generic "Help" entry point). */
  function openBrowse(): void {
    ui.openHelp();
  }

  return {
    isOpen: computed(() => ui.helpPanelOpen),
    view: computed(() => ui.helpPanelView),
    requestSeq: computed(() => ui.helpRequestSeq),
    currentSlug: computed(() => ui.helpPanelArticle),
    currentArticle,
    tree: helpTree(),
    openForRoute,
    openArticle,
    openBrowse,
    selectArticle: ui.setHelpArticle,
    close: ui.closeHelp,
  };
}
