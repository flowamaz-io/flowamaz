import { describe, it, expect, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ref } from 'vue';
import MembersView from '@/views/workspace/MembersView.vue';
import type { MemberResponse } from '@/types';

// Members whose access was removed (by SSO/SCIM or deactivation) must not look like full members.

const members = ref<MemberResponse[]>([
  { orgUserId: 'u-ana', email: 'ana@acme.test', name: 'Ana Lyst', role: 'Operator', joinedAt: '2026-10-27T00:00:00Z', isActive: false },
  { orgUserId: 'u-ben', email: 'ben@acme.test', name: 'Ben Ops', role: 'Designer', joinedAt: '2026-10-27T00:00:00Z', isActive: true },
]);
vi.mock('@/composables/useWorkspace', () => ({
  useWorkspace: () => ({
    members,
    currentWorkspaceId: ref('ws-1'),
    loadMembers: async () => undefined,
    inviteMember: vi.fn(),
    updateMemberRole: vi.fn(),
    removeMember: vi.fn(),
  }),
}));
vi.mock('@/composables/useAuth', () => ({ useAuth: () => ({ user: ref({ id: 'u-owner' }) }) }));
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));

describe('MembersView access', () => {
  it('marks members without access, lists them last and offers no role change', async () => {
    setActivePinia(createPinia());
    const wrapper = mount(MembersView, { global: { stubs: { FmModal: true, RouterLink: { template: '<a><slot /></a>' } } } });
    await flushPromises();

    const rows = wrapper.findAll('tbody tr');
    expect(rows[0]!.text()).toContain('Ben Ops');
    expect(rows[1]!.text()).toContain('Ana Lyst');
    expect(rows[1]!.find('[data-test="member-no-access"]').exists()).toBe(true);
    expect(rows[1]!.find('select').exists()).toBe(false);
    expect(rows[0]!.find('select').exists()).toBe(true);
  });
});
