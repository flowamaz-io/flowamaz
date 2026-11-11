import { describe, it, expect, vi, beforeEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useWorkspaceStore } from '@/stores/workspace.store';

// After signing in as someone else on the same browser, the previous account's workspace isn't kept.

vi.mock('@/services/workspace.service', () => ({ workspaceService: {} }));

describe('workspace store after sign-in', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('drops a remembered workspace the new account is not a member of', () => {
    localStorage.setItem('fmz_current_workspace', 'ws-previous-account');
    setActivePinia(createPinia());
    const store = useWorkspaceStore();
    store.adoptMemberships(['ws-a', 'ws-b']);
    expect(store.currentWorkspaceId).toBe('ws-a');
    expect(localStorage.getItem('fmz_current_workspace')).toBe('ws-a');
  });

  it('keeps the remembered workspace when the person is a member', () => {
    localStorage.setItem('fmz_current_workspace', 'ws-b');
    setActivePinia(createPinia());
    const store = useWorkspaceStore();
    store.adoptMemberships(['ws-a', 'ws-b']);
    expect(store.currentWorkspaceId).toBe('ws-b');
  });

  it('clears it when the person has no workspaces yet', () => {
    localStorage.setItem('fmz_current_workspace', 'ws-previous-account');
    setActivePinia(createPinia());
    const store = useWorkspaceStore();
    store.adoptMemberships([]);
    expect(store.currentWorkspaceId).toBeNull();
    expect(localStorage.getItem('fmz_current_workspace')).toBeNull();
  });
});
