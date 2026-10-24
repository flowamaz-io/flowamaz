import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import LoginView from '@/views/auth/LoginView.vue';

// B2: login honours ?redirect= (same-origin paths only) and still routes via onboarding first.

const push = vi.fn().mockResolvedValue(undefined);
const route = { query: {} as Record<string, unknown> };
vi.mock('vue-router', () => ({ useRouter: () => ({ push }), useRoute: () => route }));

const login = vi.fn().mockResolvedValue(undefined);
vi.mock('@/composables/useAuth', async () => {
  const { ref } = await import('vue');
  return { useAuth: () => ({ login, user: ref({ orgId: 'org-1' }), workspaces: ref([]) }) };
});

const onboardingDone = { value: true };
vi.mock('@/composables/useOnboarding', () => ({ isOnboardingComplete: () => onboardingDone.value }));

vi.mock('@/services/sso.service', () => ({
  ssoService: { getStatus: vi.fn().mockResolvedValue({ enabled: false, provider: null }), initiateUrl: vi.fn() },
}));

async function signIn(): Promise<void> {
  const wrapper = mount(LoginView, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } });
  await wrapper.find('input[type="email"]').setValue('owner@acme.test');
  const inputs = wrapper.findAll('input');
  const org = inputs.find((i) => i.attributes('type') !== 'email' && i.attributes('type') !== 'password');
  await org!.setValue('acme-test');
  await wrapper.find('input[type="password"]').setValue('Str0ng!Passw0rd');
  await wrapper.find('form').trigger('submit');
  await flushPromises();
}

describe('LoginView redirect', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    push.mockClear();
    login.mockClear();
    onboardingDone.value = true;
    route.query = {};
  });

  it('lands on the requested page after sign-in', async () => {
    route.query = { redirect: '/workflows' };
    await signIn();
    expect(login).toHaveBeenCalled();
    expect(push).toHaveBeenCalledWith('/workflows');
  });

  it('ignores an external redirect and lands on home', async () => {
    route.query = { redirect: 'https://evil.example/steal' };
    await signIn();
    expect(push).toHaveBeenCalledWith('/');
  });

  it('ignores a protocol-relative redirect', async () => {
    route.query = { redirect: '//evil.example' };
    await signIn();
    expect(push).toHaveBeenCalledWith('/');
  });

  it('goes through onboarding first, keeping the destination', async () => {
    onboardingDone.value = false;
    route.query = { redirect: '/workflows' };
    await signIn();
    expect(push).toHaveBeenCalledWith('/onboarding?redirect=%2Fworkflows');
  });
});
