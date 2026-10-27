import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import LoginView from '@/views/auth/LoginView.vue';

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }), useRoute: () => ({ query: {} }) }));
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

  it('hides the password field behind the owner link when the org requires SSO', async () => {
    vi.mocked(ssoService.getStatus).mockResolvedValue({ enabled: true, provider: 'oidc', required: true });
    const wrapper = mountLogin();

    (wrapper.vm as unknown as { orgSlug: string }).orgSlug = 'acme';
    await (wrapper.vm as unknown as { checkSso: () => Promise<void> }).checkSso();
    await flushPromises();

    expect(wrapper.find('[data-test="sso-button"]').exists()).toBe(true);
    expect(wrapper.find('input[type="password"]').exists()).toBe(false);

    await wrapper.find('[data-test="owner-password"]').trigger('click');
    expect(wrapper.find('input[type="password"]').exists()).toBe(true);
  });

  it('offers SSO and keeps the password when SSO is on but not required', async () => {
    vi.mocked(ssoService.getStatus).mockResolvedValue({ enabled: true, provider: 'saml', required: false });
    const wrapper = mountLogin();

    (wrapper.vm as unknown as { orgSlug: string }).orgSlug = 'acme';
    await (wrapper.vm as unknown as { checkSso: () => Promise<void> }).checkSso();
    await flushPromises();

    expect(wrapper.find('[data-test="sso-button"]').exists()).toBe(true);
    expect(wrapper.find('input[type="password"]').exists()).toBe(true);
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

describe('LoginView after a refused SSO sign-in', () => {
  it('explains a known error code in its own words and prefills the organisation', async () => {
    vi.resetModules();
    vi.doMock('vue-router', () => ({
      useRouter: () => ({ push: vi.fn() }),
      useRoute: () => ({ query: { org: 'acme', sso_error: 'SSO_NO_ACCOUNT' } }),
    }));
    setActivePinia(createPinia());
    const { default: View } = await import('@/views/auth/LoginView.vue');
    const wrapper = mount(View, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } });
    await flushPromises();

    expect(wrapper.text()).toContain("you don't have a Flowamaz account in this organisation yet");
    expect((wrapper.vm as unknown as { orgSlug: string }).orgSlug).toBe('acme');
  });

  it.each([
    ['SSO_SUBJECT_MISMATCH', 'linked to a different person'],
    ['SSO_EMAIL_UNVERIFIED', "doesn't confirm that your email address is verified"],
    ['SSO_DOMAIN_NOT_ALLOWED', 'certain email domains'],
    ['SSO_IDP_INITIATED_OFF', 'app tile'],
  ])('shows its own message for %s', async (code, text) => {
    vi.resetModules();
    vi.doMock('vue-router', () => ({
      useRouter: () => ({ push: vi.fn() }),
      useRoute: () => ({ query: { org: 'acme', sso_error: code } }),
    }));
    setActivePinia(createPinia());
    const { default: View } = await import('@/views/auth/LoginView.vue');
    const wrapper = mount(View, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } });
    await flushPromises();

    expect(wrapper.text()).toContain(text);
    expect(wrapper.text()).not.toContain("couldn't be verified");
  });
});
