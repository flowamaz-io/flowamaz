import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ref } from 'vue';
import SsoSettingsView from '@/views/settings/SsoSettingsView.vue';
import type { SsoConfig } from '@/services/sso.service';

// SSO settings: Enterprise gate, the URLs the IdP admin needs, access rules and group mappings sent on save.

let config: SsoConfig;
const testSettings = vi.fn(async () => ({ ok: true, message: 'ok' }));
let getConfig = async (): Promise<SsoConfig | null> => config;
const configure = vi.fn(async (_org: string, payload: Record<string, unknown>) => ({ ...config, ...payload }) as SsoConfig);
vi.mock('@/services/sso.service', () => ({
  ssoService: {
    getConfig: () => getConfig(),
    configure: (org: string, payload: Record<string, unknown>) => configure(org, payload),
    test: () => testSettings(),
    getScim: async () => ({ scimBaseUrl: 'https://app/api/scim/v2', tokens: [] }),
  },
}));
const authUser = ref<{ orgId: string; isOrgOwner?: boolean }>({ orgId: 'org-1', isOrgOwner: true });
vi.mock('@/composables/useAuth', () => ({ useAuth: () => ({ user: authUser }) }));
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn(), warning: vi.fn() }) }));
vi.mock('@/services/workspace.service', () => ({ workspaceService: { list: async () => ({ data: [{ id: 'ws-1', name: 'Lending', slug: 'lending', role: 'Admin' }] }) } }));

function base(overrides: Partial<SsoConfig> = {}): SsoConfig {
  return {
    orgId: 'org-1', provider: 'saml', isActive: true, idpEntityId: 'https://idp', idpSsoUrl: 'https://idp/sso',
    spEntityId: 'https://app/saml/acme', issuerUrl: null, clientId: null, scopes: [], hasCertificate: true, hasClientSecret: false,
    requireSso: false, jitProvisioning: true, defaultRole: 'Viewer', groupsAttribute: 'groups', groupRoleMappings: [],
    acsUrl: 'https://app/api/v1/saml/acme/acs', metadataUrl: 'https://app/api/v1/saml/acme/metadata',
    oidcRedirectUri: 'https://app/api/v1/oidc/acme/callback', planIncludesSso: true,
    allowIdpInitiated: false, allowedEmailDomains: [], defaultWorkspaceIds: [], rolesFromScim: false, ...overrides,
  };
}

async function mountView() {
  const wrapper = mount(SsoSettingsView, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } });
  await flushPromises();
  return wrapper;
}

describe('SsoSettingsView', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    configure.mockClear();
    testSettings.mockClear();
    authUser.value = { orgId: 'org-1', isOrgOwner: true };
    getConfig = async () => config;
  });

  it('tells a non-owner that only owners manage SSO, without loading the settings', async () => {
    config = base();
    authUser.value = { orgId: 'org-1', isOrgOwner: false };
    let loaded = false;
    getConfig = async () => { loaded = true; return config; };
    const wrapper = await mountView();
    expect(wrapper.text()).toContain('Only organisation owners manage single sign-on');
    expect(wrapper.text()).not.toContain('Not set up');
    expect(loaded).toBe(false);
  });

  it('treats a 403 from the API as not an owner, not as an error', async () => {
    authUser.value = { orgId: 'org-1' };
    getConfig = async () => {
      throw Object.assign(new Error('forbidden'), { isAxiosError: true, response: { status: 403 } });
    };
    const wrapper = await mountView();
    expect(wrapper.text()).toContain('Only organisation owners manage single sign-on');
    expect(wrapper.text()).not.toContain("Couldn't load SSO settings");
  });

  it('keeps browsers from autofilling the client ID and secret', async () => {
    config = base({ provider: 'oidc' });
    const wrapper = await mountView();
    const input = (placeholder: string) => wrapper.findAll('input').find((i) => i.attributes('placeholder') === placeholder)!;
    expect(input('your-client-id').attributes('autocomplete')).toBe('off');
    expect(wrapper.find('input[type="password"]').attributes('autocomplete')).toBe('new-password');
  });

  it('shows the Enterprise gate and no form when the plan lacks SSO', async () => {
    config = base({ planIncludesSso: false });
    const wrapper = await mountView();
    expect(wrapper.text()).toContain('Single sign-on is part of the Enterprise plan');
    expect(wrapper.find('input').exists()).toBe(false);
  });

  it('shows the ACS and metadata URLs to give the IdP', async () => {
    config = base();
    const text = (await mountView()).text();
    expect(text).toContain('https://app/api/v1/saml/acme/acs');
    expect(text).toContain('https://app/api/v1/saml/acme/metadata');
  });

  it('saves access rules and group mappings', async () => {
    config = base();
    const wrapper = await mountView();
    const checkbox = (label: string) =>
      wrapper.findAll('label').find((l) => l.text().startsWith(label))!.attributes('for');
    await wrapper.find(`#${checkbox('Require SSO')}`).setValue(true);
    await wrapper.find(`#${checkbox('Create accounts on first sign-in')}`).setValue(false);
    await wrapper.findAll('button').find((b) => b.text().includes('Add mapping'))!.trigger('click');
    const group = wrapper.findAll('input').find((i) => i.attributes('placeholder') === 'Credit-Managers')!;
    await group.setValue('Credit-Leads');
    const selects = wrapper.findAll('select');
    await selects[selects.length - 1]!.setValue('Admin');
    await wrapper.findAll('button').find((b) => b.text().includes('Save SSO settings'))!.trigger('click');
    await flushPromises();

    expect(configure).toHaveBeenCalledWith('org-1', expect.objectContaining({
      requireSso: true,
      jitProvisioning: false,
      groupRoleMappings: [{ group: 'Credit-Leads', workspaceId: null, role: 'Admin' }],
    }));
  });

  it('saves allowed domains, default workspaces and the IdP-initiated switch', async () => {
    config = base();
    const wrapper = await mountView();
    const forLabel = (label: string) => wrapper.findAll('label').find((l) => l.text().startsWith(label))!.attributes('for');
    await wrapper.find(`#${forLabel("Allow sign-in from the IdP's app tile")}`).setValue(true);
    await wrapper.findAll('input').find((i) => i.attributes('placeholder') === 'acme.com, acme.co.uk')!.setValue('acme.com,  acme.co.uk');
    await wrapper.find('#default-ws-ws-1').setValue(true);
    await wrapper.find(`#${forLabel('Take roles from SCIM groups')}`).setValue(true);
    await wrapper.findAll('button').find((b) => b.text().includes('Save SSO settings'))!.trigger('click');
    await flushPromises();

    expect(configure).toHaveBeenCalledWith('org-1', expect.objectContaining({
      allowIdpInitiated: true,
      allowedEmailDomains: ['acme.com', 'acme.co.uk'],
      defaultWorkspaceIds: ['ws-1'],
      rolesFromScim: true,
    }));
  });

  it('keeps the IdP-initiated switch off by default', async () => {
    config = base();
    const wrapper = await mountView();
    const id = wrapper.findAll('label').find((l) => l.text().startsWith("Allow sign-in from the IdP's app tile"))!.attributes('for');
    expect((wrapper.find(`#${id}`).element as HTMLInputElement).checked).toBe(false);
  });

  it('asks to save before checking unsaved edits, and checks once saved', async () => {
    config = base({ provider: 'oidc', issuerUrl: 'https://idp', clientId: 'c', hasClientSecret: true });
    const wrapper = await mountView();
    const button = (label: string) => wrapper.findAll('button').find((b) => b.text().includes(label))!;
    expect(wrapper.text()).not.toContain('Unsaved changes');

    await wrapper.findAll('input').find((i) => i.attributes('placeholder')?.startsWith('https://login.microsoftonline.com'))!
      .setValue('https://idp/typo');
    expect(wrapper.text()).toContain('Unsaved changes');
    await button('Check settings').trigger('click');
    await flushPromises();
    expect(testSettings).not.toHaveBeenCalled();

    await button('Save SSO settings').trigger('click');
    await flushPromises();
    expect(wrapper.text()).not.toContain('Unsaved changes');
    await button('Check settings').trigger('click');
    await flushPromises();
    expect(testSettings).toHaveBeenCalledTimes(1);
  });

  it('does not save a mapping without a group name', async () => {
    config = base();
    const wrapper = await mountView();
    await wrapper.findAll('button').find((b) => b.text().includes('Add mapping'))!.trigger('click');
    await wrapper.findAll('button').find((b) => b.text().includes('Save SSO settings'))!.trigger('click');
    await flushPromises();
    expect(configure).not.toHaveBeenCalled();
  });
});
