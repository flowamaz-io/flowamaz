import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { setActivePinia, createPinia } from 'pinia';
import HelpPanel from '@/components/help/HelpPanel.vue';
import { useHelp } from '@/composables/useHelp';
import { useUiStore } from '@/stores/ui.store';

vi.mock('@/services/help.service', () => ({
  helpService: { submitFeedback: vi.fn().mockResolvedValue(undefined) },
}));

function mountPanel() {
  return mount(HelpPanel, { global: { stubs: { teleport: true } } });
}

describe('HelpPanel default view', () => {
  beforeEach(() => { setActivePinia(createPinia()); });

  it('the generic Help entry opens on search + article list, even with a route article set', async () => {
    const ui = useUiStore();
    ui.setHelpArticle('getting-started/what-is-flowamaz'); // AppShell sets the route article
    const wrapper = mountPanel();
    useHelp().openBrowse();
    await flushPromises();

    expect(wrapper.find('[data-testid="help-article-view"]').exists()).toBe(false);
    expect(wrapper.find('input').isVisible()).toBe(true);
  });

  it('contextual help opens straight on the requested article', async () => {
    const wrapper = mountPanel();
    useHelp().openArticle('getting-started/what-is-flowamaz');
    await flushPromises();

    expect(wrapper.find('[data-testid="help-article-view"]').exists()).toBe(true);
  });

  it('re-opening with the generic entry returns to the list after reading an article', async () => {
    const wrapper = mountPanel();
    const help = useHelp();
    help.openArticle('getting-started/what-is-flowamaz');
    await flushPromises();
    help.close();
    help.openBrowse();
    await flushPromises();

    expect(wrapper.find('[data-testid="help-article-view"]').exists()).toBe(false);
  });

  it('shows exactly one "Was this helpful?" control per article', async () => {
    const wrapper = mountPanel();
    useHelp().openArticle('getting-started/what-is-flowamaz');
    await flushPromises();

    expect(wrapper.findAll('[aria-label="Yes, helpful"]')).toHaveLength(1);
    expect(wrapper.findAll('[aria-label="No, not helpful"]')).toHaveLength(1);
  });
});
