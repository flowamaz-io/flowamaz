import { describe, it, expect } from 'vitest';
import { mount, RouterLinkStub } from '@vue/test-utils';
import NavBar from './NavBar.vue';
import { REGISTER_URL } from '@/constants';

describe('NavBar', () => {
  it('points the CTA at the app register URL', () => {
    const wrapper = mount(NavBar, {
      global: { stubs: { RouterLink: RouterLinkStub } },
    });
    const cta = wrapper.findAll('a').find((a) => a.text() === 'Start free trial');
    expect(cta).toBeDefined();
    expect(cta!.attributes('href')).toBe(REGISTER_URL);
    expect(REGISTER_URL).toContain('app.flowamaz.io/register');
  });
});
