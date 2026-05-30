import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import HelpSearch from '@/components/help/HelpSearch.vue';
import HelpFeedbackWidget from '@/components/help/HelpFeedbackWidget.vue';

vi.mock('@/help/articleLoader', () => ({
  allArticles: () => [
    { slug: 'connectors/install', title: 'Installing connectors', section: 'Connectors', content: 'How to install a connector.' },
    { slug: 'workflows/create', title: 'Create a workflow', section: 'Workflows', content: 'Build your first workflow.' },
  ],
}));

vi.mock('@/services/help.service', () => ({
  helpService: { submitFeedback: vi.fn().mockResolvedValue(undefined) },
}));

import { helpService } from '@/services/help.service';

describe('HelpSearch', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('filters articles by the search term', async () => {
    const wrapper = mount(HelpSearch);

    await wrapper.find('input').setValue('connector');
    vi.advanceTimersByTime(300);
    await flushPromises();

    expect(wrapper.text()).toContain('Installing connectors');
    expect(wrapper.text()).not.toContain('Create a workflow');
  });
});

describe('HelpFeedbackWidget', () => {
  it('posts feedback and shows a thank-you', async () => {
    const wrapper = mount(HelpFeedbackWidget, { props: { slug: 'connectors/install' } });

    await wrapper.find('[data-test="feedback-up"]').trigger('click');
    await flushPromises();

    expect(helpService.submitFeedback).toHaveBeenCalledWith('connectors/install', true);
    expect(wrapper.find('[data-test="feedback-thanks"]').exists()).toBe(true);
  });
});
