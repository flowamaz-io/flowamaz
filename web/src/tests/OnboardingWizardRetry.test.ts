import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ref } from 'vue';
import OnboardingWizard from '@/components/onboarding/OnboardingWizard.vue';

// Creating the workspace is safe to click twice, and a retry after the workspace was already made carries on.

const createWorkspace = vi.fn();
const allWorkspaces = ref<{ id: string; slug: string; name: string }[]>([]);
vi.mock('@/composables/useWorkspace', () => ({
  useWorkspace: () => ({ createWorkspace, loadWorkspaces: async () => undefined, inviteMember: vi.fn(), allWorkspaces }),
}));
vi.mock('@/composables/useAuth', () => ({ useAuth: () => ({ user: ref({ orgId: 'org-1' }) }) }));
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));
vi.mock('@/stores/auth.store', () => ({ useAuthStore: () => ({ refreshToken: async () => undefined }) }));

function taken() {
  return Object.assign(new Error('taken'), {
    isAxiosError: true,
    response: { status: 409, data: { code: 'WORKSPACE_SLUG_TAKEN', message: "Your organisation already has a workspace with the URL 'lending'." } },
  });
}

async function fillAndMount() {
  const wrapper = mount(OnboardingWizard);
  const name = wrapper.findAll('input').find((i) => i.attributes('placeholder')?.length)!;
  await name.setValue('Lending');
  return wrapper;
}

const continueButton = (w: Awaited<ReturnType<typeof fillAndMount>>) =>
  w.findAll('button').find((b) => /continue|create/i.test(b.text()))!;

describe('OnboardingWizard workspace step', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    localStorage.clear();
    createWorkspace.mockReset();
    allWorkspaces.value = [];
  });

  it('creates the workspace once even when clicked twice', async () => {
    let finish!: () => void;
    createWorkspace.mockImplementation(() => new Promise<void>((resolve) => { finish = resolve; }));
    const wrapper = await fillAndMount();
    await continueButton(wrapper).trigger('click');
    await continueButton(wrapper).trigger('click');
    finish();
    await flushPromises();
    expect(createWorkspace).toHaveBeenCalledTimes(1);
  });

  it('carries on when the workspace already exists from an earlier click', async () => {
    createWorkspace.mockRejectedValue(taken());
    allWorkspaces.value = [{ id: 'ws-1', slug: 'lending', name: 'Lending' }];
    const wrapper = await fillAndMount();
    await continueButton(wrapper).trigger('click');
    await flushPromises();
    expect(wrapper.text()).not.toContain('already has a workspace');
  });

  it("explains the conflict when the URL belongs to a workspace the person can't open", async () => {
    createWorkspace.mockRejectedValue(taken());
    const wrapper = await fillAndMount();
    await continueButton(wrapper).trigger('click');
    await flushPromises();
    expect(wrapper.text()).toContain("already has a workspace with the URL 'lending'");
    expect(wrapper.text()).not.toContain('organisation slug');
  });
});
