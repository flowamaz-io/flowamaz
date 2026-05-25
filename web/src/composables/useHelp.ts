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

  return {
    isOpen: computed(() => ui.helpPanelOpen),
    currentSlug: computed(() => ui.helpPanelArticle),
    currentArticle,
    tree: helpTree(),
    openForRoute,
    openArticle,
    selectArticle: ui.setHelpArticle,
    close: ui.closeHelp,
  };
}
