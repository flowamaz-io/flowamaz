import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import FmEmptyState from '@/components/common/FmEmptyState.vue';

describe('FmEmptyState', () => {
  it('renders title, description, and an actionable CTA', async () => {
    const wrapper = mount(FmEmptyState, {
      props: {
        title: 'No members yet',
        description: 'Invite your teammates to collaborate.',
        ctaLabel: 'Invite your first team member',
      },
    });

    expect(wrapper.text()).toContain('No members yet');
    expect(wrapper.text()).toContain('Invite your teammates');

    const button = wrapper.find('button');
    expect(button.exists()).toBe(true);
    expect(button.text()).toContain('Invite your first team member');

    await button.trigger('click');
    expect(wrapper.emitted('cta')).toBeTruthy();
  });

  it('omits the CTA button when no ctaLabel is provided', () => {
    const wrapper = mount(FmEmptyState, {
      props: { title: 'Empty', description: 'Nothing here, but here is what to do next.' },
    });
    expect(wrapper.find('button').exists()).toBe(false);
  });
});
