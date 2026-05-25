import { http, unwrap } from './api.service';
import type {
  AiConfigView,
  ApiEnvelope,
  ApiKeyResponse,
  AddMemberRequest,
  CreateApiKeyRequest,
  CreateWorkspaceRequest,
  MemberResponse,
  PagedResult,
  UpdateAiConfigRequest,
  UpdateRoleRequest,
  UpdateWorkspaceSettingsRequest,
  WorkspaceListItem,
  WorkspaceResponse,
} from '@/types';

export const workspaceService = {
  async list(): Promise<PagedResult<WorkspaceListItem>> {
    const res = await http.get<ApiEnvelope<PagedResult<WorkspaceListItem>>>('/api/v1/workspaces');
    return unwrap(res);
  },

  async create(payload: CreateWorkspaceRequest): Promise<WorkspaceResponse> {
    const res = await http.post<ApiEnvelope<WorkspaceResponse>>('/api/v1/workspaces', payload);
    return unwrap(res);
  },

  async get(id: string): Promise<WorkspaceResponse> {
    const res = await http.get<ApiEnvelope<WorkspaceResponse>>(`/api/v1/workspaces/${id}`);
    return unwrap(res);
  },

  async updateSettings(id: string, payload: UpdateWorkspaceSettingsRequest): Promise<WorkspaceResponse> {
    const res = await http.put<ApiEnvelope<WorkspaceResponse>>(`/api/v1/workspaces/${id}/settings`, payload);
    return unwrap(res);
  },

  async remove(id: string): Promise<void> {
    await http.delete(`/api/v1/workspaces/${id}`);
  },

  // ── AI config ────────────────────────────────────────────────────────────
  async getAiConfig(id: string): Promise<AiConfigView> {
    const res = await http.get<ApiEnvelope<AiConfigView>>(`/api/v1/workspaces/${id}/ai-config`);
    return unwrap(res);
  },

  async updateAiConfig(id: string, payload: UpdateAiConfigRequest): Promise<AiConfigView> {
    const res = await http.put<ApiEnvelope<AiConfigView>>(`/api/v1/workspaces/${id}/ai-config`, payload);
    return unwrap(res);
  },

  // ── Members ──────────────────────────────────────────────────────────────
  async listMembers(workspaceId: string): Promise<PagedResult<MemberResponse>> {
    const res = await http.get<ApiEnvelope<PagedResult<MemberResponse>>>(
      `/api/v1/workspaces/${workspaceId}/members`,
    );
    return unwrap(res);
  },

  async addMember(workspaceId: string, payload: AddMemberRequest): Promise<MemberResponse> {
    const res = await http.post<ApiEnvelope<MemberResponse>>(
      `/api/v1/workspaces/${workspaceId}/members`,
      payload,
    );
    return unwrap(res);
  },

  async updateMemberRole(workspaceId: string, userId: string, payload: UpdateRoleRequest): Promise<void> {
    await http.patch(`/api/v1/workspaces/${workspaceId}/members/${userId}/role`, payload);
  },

  async removeMember(workspaceId: string, userId: string): Promise<void> {
    await http.delete(`/api/v1/workspaces/${workspaceId}/members/${userId}`);
  },

  // ── API keys ─────────────────────────────────────────────────────────────
  async listApiKeys(workspaceId: string): Promise<PagedResult<ApiKeyResponse>> {
    const res = await http.get<ApiEnvelope<PagedResult<ApiKeyResponse>>>(
      `/api/v1/workspaces/${workspaceId}/api-keys`,
    );
    return unwrap(res);
  },

  async createApiKey(workspaceId: string, payload: CreateApiKeyRequest): Promise<ApiKeyResponse> {
    const res = await http.post<ApiEnvelope<ApiKeyResponse>>(
      `/api/v1/workspaces/${workspaceId}/api-keys`,
      payload,
    );
    return unwrap(res);
  },

  async revokeApiKey(workspaceId: string, keyId: string): Promise<void> {
    await http.delete(`/api/v1/workspaces/${workspaceId}/api-keys/${keyId}`);
  },
};
