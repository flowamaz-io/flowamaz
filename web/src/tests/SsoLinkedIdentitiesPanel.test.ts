import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import SsoLinkedIdentitiesPanel from '@/components/settings/SsoLinkedIdentitiesPanel.vue';
import type { SsoLinkedIdentityPage } from '@/services/sso.service';

// Linked sign-ins: list, search, unlink with confirmation, empty and error states.

let page: SsoLinkedIdentityPage;
let failLoad = false;
const getLinked = vi.fn(async (_org: string, _search?: string) => {
  if (failLoad) throw new Error('down');
  return page;
});
const unlink = vi.fn(async (_org: string, _id: string) => undefined);
vi.mock('@/services/sso.service', () => ({
  ssoService: {
    getLinkedIdentities: (org: string, search?: string) => getLinked(org, search),
    unlinkIdentity: (org: string, id: string) => unlink(org, id),
  },
}));
const toastSuccess = vi.fn();
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ success: toastSuccess, error: vi.fn() }) }));
vi.mock('@/utils/error.util', () => ({ toUserFacingError: () => ({ message: 'Linked sign-ins are unavailable. Try again.' }) }));

const ana = { orgUserId: 'u1', email: 'ana@acme.com', name: 'Ana', isOrgOwner: false, lastLoginAt: '2026-11-01T09:00:00Z' };
const owner = { orgUserId: 'me', email: 'owner@acme.com', name: 'Owner', isOrgOwner: true, lastLoginAt: null };

async function mountPanel() {
  const wrapper = mount(SsoLinkedIdentitiesPanel, { props: { orgId: 'org-1', currentUserId: 'me' } });
  await flushPromises();
  return wrapper;
}

describe('SsoLinkedIdentitiesPanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    failLoad = false;
    page = { items: [ana, owner], total: 2 };
    getLinked.mockClear();
    unlink.mockClear();
    toastSuccess.mockClear();
  });

  it('lists linked people and marks the current user', async () => {
    const wrapper = await mountPanel();
    const rows = wrapper.findAll('[data-test="sso-identity"]');
    expect(rows).toHaveLength(2);
    expect(rows[1]!.text()).toContain('· You');
    expect(rows[1]!.text()).toContain('Not signed in yet');
  });

  it('asks before unlinking, then removes the person from the list', async () => {
    const wrapper = await mountPanel();
    await wrapper.find('[aria-label="Unlink ana@acme.com"]').trigger('click');
    expect(wrapper.text()).toContain('They are signed out');
    expect(unlink).not.toHaveBeenCalled();
    const confirm = wrapper.find('[aria-label="Confirm unlinking ana@acme.com"]');
    await confirm.findAll('button').find((b) => b.text() === 'Unlink')!.trigger('click');
    await flushPromises();
    expect(unlink).toHaveBeenCalledWith('org-1', 'u1');
    expect(wrapper.findAll('[data-test="sso-identity"]')).toHaveLength(1);
    expect(toastSuccess).toHaveBeenCalled();
  });

  it('teaches what happens when nobody is linked yet', async () => {
    page = { items: [], total: 0 };
    const wrapper = await mountPanel();
    expect(wrapper.find('[data-test="sso-identities-empty"]').text()).toContain('the first time they sign in with SSO');
  });

  it('shows a way to retry when the list fails to load', async () => {
    failLoad = true;
    const wrapper = await mountPanel();
    expect(wrapper.text()).toContain("Couldn't load linked sign-ins");
    failLoad = false;
    await wrapper.find('button').trigger('click');
    await flushPromises();
    expect(wrapper.findAll('[data-test="sso-identity"]')).toHaveLength(2);
  });
});
