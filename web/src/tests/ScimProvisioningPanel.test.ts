import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import ScimProvisioningPanel from '@/components/settings/ScimProvisioningPanel.vue';
import type { ScimSetup } from '@/services/sso.service';

// SCIM tokens: base URL to copy, token shown once after creating, list without the token, revoke, empty and error states.

let setup: ScimSetup;
let failLoad = false;
const create = vi.fn<(org: string, name: string) => Promise<unknown>>();
const revoke = vi.fn(async (_org: string, _id: string) => undefined);
vi.mock('@/services/sso.service', () => ({
  ssoService: {
    getScim: async () => {
      if (failLoad) throw new Error('down');
      return setup;
    },
    createScimToken: (org: string, name: string) => create(org, name),
    revokeScimToken: (org: string, id: string) => revoke(org, id),
  },
}));
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));
vi.mock('@/utils/error.util', () => ({ toUserFacingError: () => ({ message: 'SCIM is unavailable. Try again.' }) }));

const token = { id: 't1', name: 'Okta', tokenPrefix: 'fmzscim_abcdef', createdAt: '2026-10-27T01:00:00Z', lastUsedAt: null };

async function mountPanel() {
  const wrapper = mount(ScimProvisioningPanel, { props: { orgId: 'org-1' } });
  await flushPromises();
  return wrapper;
}

describe('ScimProvisioningPanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    failLoad = false;
    setup = { scimBaseUrl: 'https://app/api/scim/v2', tokens: [] };
    create.mockReset();
    revoke.mockClear();
  });

  it('teaches what to do when there are no tokens', async () => {
    const wrapper = await mountPanel();
    expect(wrapper.find('[data-test="scim-base-url"]').text()).toBe('https://app/api/scim/v2');
    expect(wrapper.text()).toContain('No SCIM tokens yet');
  });

  it('shows a new token once, and lists only its prefix', async () => {
    create.mockResolvedValue({ token, plainToken: 'fmzscim_abcdefSECRET', scimBaseUrl: 'https://app/api/scim/v2' });
    const wrapper = await mountPanel();
    await wrapper.findAll('input').find((i) => i.attributes('placeholder') === 'Okta production')!.setValue('Okta');
    await wrapper.findAll('button').find((b) => b.text().includes('Create token'))!.trigger('click');
    await flushPromises();

    expect(create).toHaveBeenCalledWith('org-1', 'Okta');
    expect(wrapper.find('[data-test="scim-new-token"]').text()).toBe('fmzscim_abcdefSECRET');
    expect(wrapper.text()).toContain("you won't see it again");

    await wrapper.findAll('button').find((b) => b.text() === 'Done')!.trigger('click');
    expect(wrapper.text()).not.toContain('SECRET');
    expect(wrapper.text()).toContain('fmzscim_abcdef…');
  });

  it('revokes a token', async () => {
    setup = { scimBaseUrl: 'https://app/api/scim/v2', tokens: [token] };
    const wrapper = await mountPanel();
    await wrapper.find('button[aria-label="Revoke Okta"]').trigger('click');
    await flushPromises();
    expect(revoke).toHaveBeenCalledWith('org-1', 't1');
    expect(wrapper.text()).toContain('No SCIM tokens yet');
  });

  it('shows an error with a retry when tokens fail to load', async () => {
    failLoad = true;
    const wrapper = await mountPanel();
    expect(wrapper.text()).toContain("Couldn't load SCIM tokens");
  });
});
