import { storeToRefs } from 'pinia';
import { useWorkspaceStore } from '@/stores/workspace.store';

/** Ergonomic accessor for workspace state + actions in components. */
export function useWorkspace() {
  const store = useWorkspaceStore();
  const refs = storeToRefs(store);
  return {
    ...refs,
    loadWorkspaces: store.loadWorkspaces,
    switchWorkspace: store.switchWorkspace,
    createWorkspace: store.createWorkspace,
    loadCurrentWorkspace: store.loadCurrentWorkspace,
    updateSettings: store.updateSettings,
    loadMembers: store.loadMembers,
    inviteMember: store.inviteMember,
    updateMemberRole: store.updateMemberRole,
    removeMember: store.removeMember,
    loadEnvironments: store.loadEnvironments,
    loadApiKeys: store.loadApiKeys,
    createApiKey: store.createApiKey,
    revokeApiKey: store.revokeApiKey,
    loadAiConfig: store.loadAiConfig,
    updateAiConfig: store.updateAiConfig,
  };
}
