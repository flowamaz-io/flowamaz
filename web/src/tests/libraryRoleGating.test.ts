import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import ConnectorCard from '@/components/library/ConnectorCard.vue';
import LibraryFormsPanel from '@/components/library/LibraryFormsPanel.vue';
import type { ConnectorDefinition } from '@/services/connector.service';

// Viewer gating in the Library (Jagan's browser check after PR #24).
const role = { value: 'Viewer' as string };
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn(async () => undefined) }) }));
vi.mock('@/composables/useWorkspace', () => ({
  useWorkspace: () => ({ currentWorkspaceId: { value: 'ws-1' }, current: { value: { role: role.value } }, loadWorkspaces: vi.fn() }),
}));
vi.mock('@/services/library.service', () => ({
  noticeDate: () => '',
  libraryService: {
    listForms: vi.fn(async () => [{
      id: 'f2', slug: 'vendor-check', name: 'Vendor check', description: '', category: 'Custom', version: '1.0.0',
      publisher: null, isOfficial: false, isWorkspaceForm: true, tags: [], deprecatedAt: null, deprecationNote: null,
      fields: [{ key: 'vat', label: 'VAT', type: 'text' }],
    }]),
    deleteForm: vi.fn(),
  },
}));

const slack: ConnectorDefinition = {
  id: 'd', connectorId: 'slack', publisherId: 'flowamaz-io', displayName: 'Slack', version: '1.1.0', category: 'Messaging',
  tags: [], manifestJson: '{}', tier: 'Official', isEnabled: true, isInstalled: false, installCount: 0, averageRating: 0,
  ratingCount: 0, isOfficial: true,
};

describe('Library role gating', () => {
  beforeEach(() => { role.value = 'Viewer'; });

  it('a connector card offers no Install below Designer', () => {
    const viewer = mount(ConnectorCard, { props: { connector: slack, isInstalled: false, canInstall: false } });
    expect(viewer.find('button').exists()).toBe(false);
    expect(viewer.find('[data-testid="connector-install-role-note"]').text()).toBe('Designer role needed');
    const designer = mount(ConnectorCard, { props: { connector: slack, isInstalled: false } });
    expect(designer.find('button').text()).toBe('Install');
  });

  it("only a Designer or higher can delete the workspace's own form", async () => {
    const viewer = mount(LibraryFormsPanel);
    await flushPromises();
    expect(viewer.text()).toContain('Vendor check');
    expect(viewer.text()).not.toContain('Delete form');

    role.value = 'Designer';
    const designer = mount(LibraryFormsPanel);
    await flushPromises();
    expect(designer.text()).toContain('Delete form');
  });
});
