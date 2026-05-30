import { describe, it, expect } from 'vitest';
import { mount, RouterLinkStub } from '@vue/test-utils';
import HomeView from './HomeView.vue';

describe('HomeView', () => {
  it('renders the hero headline without errors', () => {
    const wrapper = mount(HomeView, {
      global: { stubs: { RouterLink: RouterLinkStub } },
    });
    expect(wrapper.text()).toContain('Automate anything. In plain English.');
    expect(wrapper.text()).toContain('How it works');
  });
});
