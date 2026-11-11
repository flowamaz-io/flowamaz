import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ref } from 'vue';
import AuditLogView from '@/views/settings/AuditLogView.vue';

// Owners can switch the audit log to the whole organisation, which includes SSO and SCIM events (no workspace).

const page = { data: [], pagination: { page: 1, pageSize: 20, total: 0, totalPages: 0 } };
const list = vi.fn(async (_ws: string, _q: unknown) => page);
const listOrg = vi.fn(async (_org: string, _q: unknown) => page);
vi.mock('@/services/audit.service', () => ({
  auditService: { list: (ws: string, q: unknown) => list(ws, q), listOrg: (org: string, q: unknown) => listOrg(org, q), exportCsv: vi.fn(), exportOrgCsv: vi.fn() },
}));
const user = ref<{ orgId: string; isOrgOwner?: boolean }>({ orgId: 'org-1', isOrgOwner: true });
vi.mock('@/composables/useAuth', () => ({ useAuth: () => ({ user }) }));
vi.mock('@/composables/useWorkspace', () => ({ useWorkspace: () => ({ currentWorkspaceId: ref('ws-1') }) }));
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));

async function mountView() {
  const wrapper = mount(AuditLogView);
  await flushPromises();
  return wrapper;
}

describe('AuditLogView scope', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    list.mockClear();
    listOrg.mockClear();
    user.value = { orgId: 'org-1', isOrgOwner: true };
  });

  it('lets an owner see the whole organisation', async () => {
    const wrapper = await mountView();
    expect(list).toHaveBeenCalledWith('ws-1', expect.anything());
    await wrapper.findAll('button[role="radio"]').find((b) => b.text() === 'Whole organisation')!.trigger('click');
    await flushPromises();
    expect(listOrg).toHaveBeenCalledWith('org-1', expect.anything());
    expect(wrapper.text()).toContain('organisation settings (SSO, SCIM, billing)');
  });

  it('offers the SCIM event filters', async () => {
    const wrapper = await mountView();
    expect(wrapper.text()).toContain('SCIM: user deactivated');
    expect(wrapper.text()).toContain('Instance cancelled');
  });

  it('has no organisation switch for non-owners', async () => {
    user.value = { orgId: 'org-1', isOrgOwner: false };
    const wrapper = await mountView();
    expect(wrapper.find('[aria-label="Audit log scope"]').exists()).toBe(false);
  });
});
