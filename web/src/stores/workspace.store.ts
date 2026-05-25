import { defineStore } from 'pinia';
import { computed, ref } from 'vue';
import { workspaceService } from '@/services/workspace.service';
import type {
  AddMemberRequest,
  AiConfigView,
  ApiKeyResponse,
  CreateApiKeyRequest,
  CreateWorkspaceRequest,
  MemberResponse,
  UpdateAiConfigRequest,
  UpdateWorkspaceSettingsRequest,
  WorkspaceEnvironment,
  WorkspaceListItem,
  WorkspaceResponse,
  WorkspaceRole,
} from '@/types';

const CURRENT_WS_KEY = 'fmz_current_workspace';

export const useWorkspaceStore = defineStore('workspace', () => {
  const allWorkspaces = ref<WorkspaceListItem[]>([]);
  const currentWorkspaceId = ref<string | null>(localStorage.getItem(CURRENT_WS_KEY));
  const currentWorkspace = ref<WorkspaceResponse | null>(null);
  const members = ref<MemberResponse[]>([]);
  const apiKeys = ref<ApiKeyResponse[]>([]);
  const environments = ref<WorkspaceEnvironment[]>([]);
  const aiConfig = ref<AiConfigView | null>(null);

  const current = computed<WorkspaceListItem | null>(
    () => allWorkspaces.value.find((w) => w.id === currentWorkspaceId.value) ?? null,
  );

  async function loadWorkspaces(): Promise<void> {
    const result = await workspaceService.list();
    allWorkspaces.value = result.data;
    if (!currentWorkspaceId.value && result.data.length > 0) {
      switchWorkspace(result.data[0]!.id);
    } else if (
      currentWorkspaceId.value &&
      !result.data.some((w) => w.id === currentWorkspaceId.value) &&
      result.data.length > 0
    ) {
      switchWorkspace(result.data[0]!.id);
    }
  }

  function switchWorkspace(id: string): void {
    currentWorkspaceId.value = id;
    localStorage.setItem(CURRENT_WS_KEY, id);
    // Invalidate per-workspace caches so views reload fresh.
    members.value = [];
    apiKeys.value = [];
    environments.value = [];
    aiConfig.value = null;
    currentWorkspace.value = null;
  }

  async function createWorkspace(payload: CreateWorkspaceRequest): Promise<WorkspaceResponse> {
    const ws = await workspaceService.create(payload);
    allWorkspaces.value.push({ id: ws.id, name: ws.name, slug: ws.slug, role: 'Admin' });
    switchWorkspace(ws.id);
    return ws;
  }

  async function loadCurrentWorkspace(): Promise<void> {
    if (!currentWorkspaceId.value) return;
    currentWorkspace.value = await workspaceService.get(currentWorkspaceId.value);
  }

  async function updateSettings(payload: UpdateWorkspaceSettingsRequest): Promise<void> {
    if (!currentWorkspaceId.value) return;
    currentWorkspace.value = await workspaceService.updateSettings(currentWorkspaceId.value, payload);
  }

  // ── Members ────────────────────────────────────────────────────────────────
  async function loadMembers(): Promise<void> {
    if (!currentWorkspaceId.value) return;
    const result = await workspaceService.listMembers(currentWorkspaceId.value);
    members.value = result.data;
  }

  async function inviteMember(payload: AddMemberRequest): Promise<void> {
    if (!currentWorkspaceId.value) return;
    const member = await workspaceService.addMember(currentWorkspaceId.value, payload);
    members.value.push(member);
  }

  async function updateMemberRole(userId: string, role: WorkspaceRole): Promise<void> {
    if (!currentWorkspaceId.value) return;
    await workspaceService.updateMemberRole(currentWorkspaceId.value, userId, { role });
    const m = members.value.find((x) => x.orgUserId === userId);
    if (m) m.role = role;
  }

  async function removeMember(userId: string): Promise<void> {
    if (!currentWorkspaceId.value) return;
    await workspaceService.removeMember(currentWorkspaceId.value, userId);
    members.value = members.value.filter((m) => m.orgUserId !== userId);
  }

  // ── Environments ───────────────────────────────────────────────────────────
  async function loadEnvironments(): Promise<void> {
    if (!currentWorkspaceId.value) return;
    environments.value = await workspaceService.getEnvironments(currentWorkspaceId.value);
  }

  // ── API keys ─────────────────────────────────────────────────────────────
  async function loadApiKeys(): Promise<void> {
    if (!currentWorkspaceId.value) return;
    const result = await workspaceService.listApiKeys(currentWorkspaceId.value);
    apiKeys.value = result.data;
  }

  async function createApiKey(payload: CreateApiKeyRequest): Promise<ApiKeyResponse> {
    if (!currentWorkspaceId.value) throw new Error('No workspace selected.');
    const key = await workspaceService.createApiKey(currentWorkspaceId.value, payload);
    // Store the list-safe copy (plainKey null) — the caller shows the plain key once.
    apiKeys.value.push({ ...key, plainKey: null });
    return key;
  }

  async function revokeApiKey(keyId: string): Promise<void> {
    if (!currentWorkspaceId.value) return;
    await workspaceService.revokeApiKey(currentWorkspaceId.value, keyId);
    apiKeys.value = apiKeys.value.filter((k) => k.id !== keyId);
  }

  // ── AI config ─────────────────────────────────────────────────────────────
  async function loadAiConfig(): Promise<void> {
    if (!currentWorkspaceId.value) return;
    aiConfig.value = await workspaceService.getAiConfig(currentWorkspaceId.value);
  }

  async function updateAiConfig(payload: UpdateAiConfigRequest): Promise<void> {
    if (!currentWorkspaceId.value) return;
    aiConfig.value = await workspaceService.updateAiConfig(currentWorkspaceId.value, payload);
  }

  return {
    allWorkspaces,
    currentWorkspaceId,
    currentWorkspace,
    members,
    apiKeys,
    environments,
    aiConfig,
    current,
    loadWorkspaces,
    switchWorkspace,
    createWorkspace,
    loadCurrentWorkspace,
    updateSettings,
    loadMembers,
    inviteMember,
    updateMemberRole,
    removeMember,
    loadEnvironments,
    loadApiKeys,
    createApiKey,
    revokeApiKey,
    loadAiConfig,
    updateAiConfig,
  };
});
