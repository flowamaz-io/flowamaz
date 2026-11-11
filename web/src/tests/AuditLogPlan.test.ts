import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ref } from 'vue';
import AuditLogView from '@/views/settings/AuditLogView.vue';
import type { AuditHistory } from '@/services/audit.service';

// The audit page follows the plan: how far back it shows, and export / organisation view only where included.

let history: AuditHistory | null = null;
const list = vi.fn(async () => ({ data: [], pagination: { page: 1, pageSize: 20, total: 0, totalPages: 0 }, history }));
vi.mock('@/services/audit.service', () => ({
  auditService: { list: () => list(), listOrg: () => list(), exportCsv: vi.fn(), exportOrgCsv: vi.fn() },
}));
vi.mock('@/composables/useAuth', () => ({ useAuth: () => ({ user: ref({ orgId: 'org-1', isOrgOwner: true }) }) }));
vi.mock('@/composables/useWorkspace', () => ({ useWorkspace: () => ({ currentWorkspaceId: ref('ws-1') }) }));
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));

async function mountView() {
  const wrapper = mount(AuditLogView, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } });
  await flushPromises();
  return wrapper;
}

describe('AuditLogView and the plan', () => {
  beforeEach(() => setActivePinia(createPinia()));

  it('on Community, says how far back it shows and offers no export or organisation view', async () => {
    history = { planName: 'Community', days: 7, startsAt: '2026-11-04T00:00:00Z', canExport: false };
    const wrapper = await mountView();
    expect(wrapper.find('[data-test="audit-plan-notice"]').text()).toContain('last 7 days');
    expect(wrapper.text()).toContain('Older activity is kept');
    expect(wrapper.text()).not.toContain('Export CSV');
    expect(wrapper.find('[aria-label="Audit log scope"]').exists()).toBe(false);
  });

  it('on a paid plan, offers export and the organisation view', async () => {
    history = { planName: 'Pro', days: 365, startsAt: '2025-11-11T00:00:00Z', canExport: true };
    const wrapper = await mountView();
    expect(wrapper.text()).toContain('Export CSV');
    expect(wrapper.find('[aria-label="Audit log scope"]').exists()).toBe(true);
    expect(wrapper.find('[data-test="audit-plan-notice"]').text()).toContain('last year');
  });

  it('with full history, shows no notice', async () => {
    history = { planName: 'Enterprise', days: 0, startsAt: null, canExport: true };
    const wrapper = await mountView();
    expect(wrapper.find('[data-test="audit-plan-notice"]').exists()).toBe(false);
  });
});
