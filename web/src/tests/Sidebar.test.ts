import { describe, expect, it, vi, beforeEach } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import Sidebar from '@/components/layout/Sidebar.vue';
import { useWorkspaceStore } from '@/stores/workspace.store';

// Gate service is called on mount for the pending badge — stub it so the test is hermetic.
vi.mock('@/services/gate.service', () => ({
  gateService: { getPendingGates: vi.fn().mockResolvedValue([]) },
}));

function mountSidebar() {
  return mount(Sidebar, {
    global: {
      stubs: {
        RouterLink: { template: '<a><slot /></a>' },
        WorkspaceSwitcher: { template: '<div data-test="ws-switcher" />' },
        UserMenu: { template: '<div data-test="user-menu" />' },
      },
    },
  });
}

describe('Sidebar', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    const store = useWorkspaceStore();
    store.allWorkspaces = [{ id: 'w1', name: 'Finance Team', slug: 'finance', role: 'Admin' }];
    store.currentWorkspaceId = 'w1';
  });

  it('renders the dark sidebar background and brand name', () => {
    const wrapper = mountSidebar();
    const aside = wrapper.find('aside');
    expect(aside.classes()).toContain('bg-sidebar-bg');
    expect(wrapper.text()).toContain('Flowamaz');
  });

  it('renders the main navigation items and section labels', () => {
    const wrapper = mountSidebar();
    const text = wrapper.text();
    for (const label of ['Dashboard', 'Workflows', 'Instances', 'Gates', 'Weather', 'Library', 'Analytics', 'Settings', 'Help']) {
      expect(text).toContain(label);
    }
    expect(text.toUpperCase()).toContain('MAIN');
  });

  it('embeds the workspace switcher and user menu', () => {
    const wrapper = mountSidebar();
    expect(wrapper.find('[data-test="ws-switcher"]').exists()).toBe(true);
    expect(wrapper.find('[data-test="user-menu"]').exists()).toBe(true);
  });

  it('applies teal active styling on internal links', () => {
    const wrapper = mountSidebar();
    const dashboard = wrapper
      .findAll('a')
      .find((a) => a.text().includes('Dashboard'));
    expect(dashboard).toBeTruthy();
    expect(dashboard!.attributes('active-class')).toContain('border-sidebar-active');
  });
});
