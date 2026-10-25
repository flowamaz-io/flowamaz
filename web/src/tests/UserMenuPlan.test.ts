import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ref } from 'vue';
import UserMenu from '@/components/layout/UserMenu.vue';
import { useWorkspaceStore } from '@/stores/workspace.store';

// Run-6 finding 3: the badge shows the organisation's billing plan (which sets its limits), not the server's
// EDITION setting. A self-hosted Community Edition install has no plan and says so.

// A plain function (not vi.fn with mockReset): each test sets what billing answers.
let answer: () => Promise<{ planName: string; planSlug: string }> = () => Promise.resolve({ planName: 'Community', planSlug: 'community' });
let calls = 0;
vi.mock('@/services/billing.service', () => ({ billingService: { current: () => { calls++; return answer(); } } }));
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock('@/composables/useAuth', () => ({
  useAuth: () => ({ user: ref({ id: 'u1', name: 'Ada', email: 'a@x.test', orgId: 'org-1' }), logout: vi.fn() }),
}));

async function badgeFor(edition: string): Promise<string> {
  setActivePinia(createPinia());
  const store = useWorkspaceStore();
  store.currentWorkspaceId = 'ws-1';
  store.currentWorkspace = { id: 'ws-1', edition } as never;
  const wrapper = mount(UserMenu, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } });
  await flushPromises();
  return wrapper.text();
}

describe('UserMenu plan badge', () => {
  beforeEach(() => { calls = 0; });

  it("shows the organisation's plan on the cloud, whatever the server edition", async () => {
    answer = () => Promise.resolve({ planName: 'Community', planSlug: 'community' });
    const text = await badgeFor('pro');
    expect(text).toContain('Community plan');
    expect(text).not.toContain('Pro');
  });

  it('says Community Edition on a self-hosted install, without asking billing', async () => {
    const text = await badgeFor('community');
    expect(text).toContain('Community Edition');
    expect(calls).toBe(0);
  });

  it('shows no badge when the plan cannot be loaded', async () => {
    answer = () => Promise.reject(new Error('offline'));
    const text = await badgeFor('pro');
    expect(text).not.toMatch(/plan|Plan/);
  });
});
