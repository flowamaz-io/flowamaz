import { describe, expect, it, beforeEach, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import WorkspaceSwitcher from '@/components/layout/WorkspaceSwitcher.vue';
import { useWorkspaceStore } from '@/stores/workspace.store';

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

function mountSwitcher() {
  return mount(WorkspaceSwitcher, {
    global: {
      stubs: {
        RouterLink: { template: '<a><slot /></a>' },
      },
    },
  });
}

describe('WorkspaceSwitcher', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    const store = useWorkspaceStore();
    store.allWorkspaces = [
      { id: 'w1', name: 'Finance Team', slug: 'finance', role: 'Admin' },
      { id: 'w2', name: 'Marketing', slug: 'marketing', role: 'Designer' },
    ];
    store.currentWorkspaceId = 'w1';
  });

  it('shows the current workspace name in the trigger', () => {
    const wrapper = mountSwitcher();
    expect(wrapper.text()).toContain('Finance Team');
  });

  it('opens the dropdown and lists all workspaces', async () => {
    const wrapper = mountSwitcher();
    expect(wrapper.find('input').exists()).toBe(false);
    await wrapper.find('button').trigger('click');
    expect(wrapper.find('input').exists()).toBe(true);
    expect(wrapper.text()).toContain('Finance Team');
    expect(wrapper.text()).toContain('Marketing');
    expect(wrapper.text()).toContain('New Workspace');
  });

  it('filters workspaces by the search query', async () => {
    const wrapper = mountSwitcher();
    await wrapper.find('button').trigger('click');
    await wrapper.find('input').setValue('mark');
    // Scope to the dropdown list — the trigger always shows the current workspace name.
    const list = wrapper.find('ul');
    expect(list.text()).toContain('Marketing');
    expect(list.text()).not.toContain('Finance Team');
  });

  it('switches the workspace on selection', async () => {
    const store = useWorkspaceStore();
    const wrapper = mountSwitcher();
    await wrapper.find('button').trigger('click');
    const marketing = wrapper.findAll('button').find((b) => b.text() === 'Marketing');
    expect(marketing).toBeTruthy();
    await marketing!.trigger('click');
    expect(store.currentWorkspaceId).toBe('w2');
  });
});
