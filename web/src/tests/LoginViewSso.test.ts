import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import LoginView from '@/views/auth/LoginView.vue';

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock('@/services/sso.service', () => ({
  ssoService: {
    getStatus: vi.fn(),
    initiateUrl: vi.fn().mockReturnValue('https://api/sso'),
  },
}));

import { ssoService } from '@/services/sso.service';

function mountLogin() {
  return mount(LoginView, {
    global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } },
  });
}

describe('LoginView SSO detection', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.clearAllMocks();
  });

  it('hides the password field and shows the SSO button when the org enforces SSO', async () => {
    vi.mocked(ssoService.getStatus).mockResolvedValue({ enabled: true, provider: 'oidc' });
    const wrapper = mountLogin();

    (wrapper.vm as unknown as { orgSlug: string }).orgSlug = 'acme';
    await (wrapper.vm as unknown as { checkSso: () => Promise<void> }).checkSso();
    await flushPromises();

    expect(wrapper.find('[data-test="sso-button"]').exists()).toBe(true);
    expect(wrapper.find('input[type="password"]').exists()).toBe(false);
  });

  it('keeps the password field for non-SSO orgs', async () => {
    vi.mocked(ssoService.getStatus).mockResolvedValue({ enabled: false, provider: null });
    const wrapper = mountLogin();

    (wrapper.vm as unknown as { orgSlug: string }).orgSlug = 'acme';
    await (wrapper.vm as unknown as { checkSso: () => Promise<void> }).checkSso();
    await flushPromises();

    expect(wrapper.find('[data-test="sso-button"]').exists()).toBe(false);
    expect(wrapper.find('input[type="password"]').exists()).toBe(true);
  });
});
