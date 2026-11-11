import { describe, it, expect, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import RegisterView from '@/views/auth/RegisterView.vue';

// The plan cards are a radio group for screen readers and keyboards.

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }), useRoute: () => ({ query: {} }) }));

describe('Registration plan cards', () => {
  it('are a labelled radio group with one checked plan, moved by arrow keys', async () => {
    setActivePinia(createPinia());
    const wrapper = mount(RegisterView, { attachTo: document.body, global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } });

    const group = wrapper.find('[role="radiogroup"]');
    expect(group.attributes('aria-labelledby')).toBe('plan-legend');
    expect(wrapper.find('#plan-legend').text()).toBe('Choose a plan');

    const radios = () => wrapper.findAll('[role="radio"]');
    const checked = () => radios().filter((r) => r.attributes('aria-checked') === 'true');
    expect(checked()).toHaveLength(1);
    expect(checked()[0]!.attributes('data-plan')).toBe('community');
    expect(radios().filter((r) => r.attributes('tabindex') === '0')).toHaveLength(1);

    await group.trigger('keydown', { key: 'ArrowRight' });
    expect(checked()[0]!.attributes('data-plan')).not.toBe('community');
    expect(checked()[0]!.attributes('tabindex')).toBe('0');
    wrapper.unmount();
  });
});
