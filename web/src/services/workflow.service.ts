import { http, unwrap } from './api.service';
import type {
  ApiEnvelope,
  CreateWorkflowDefinitionRequest,
  PagedResult,
  UpdateWorkflowDefinitionRequest,
  WorkflowAtCommitResponse,
  WorkflowCommit,
  WorkflowDefinitionListItem,
  WorkflowDefinitionResponse,
  WorkflowDiff,
  WorkflowVersionResponse,
} from '@/types';

const base = (workspaceId: string): string => `/api/v1/workspaces/${workspaceId}/workflows`;

export const workflowService = {
  async list(workspaceId: string, page = 1, pageSize = 20): Promise<PagedResult<WorkflowDefinitionListItem>> {
    const res = await http.get<ApiEnvelope<PagedResult<WorkflowDefinitionListItem>>>(
      base(workspaceId),
      { params: { page, pageSize } },
    );
    return unwrap(res);
  },

  async create(workspaceId: string, payload: CreateWorkflowDefinitionRequest): Promise<WorkflowDefinitionResponse> {
    const res = await http.post<ApiEnvelope<WorkflowDefinitionResponse>>(base(workspaceId), payload);
    return unwrap(res);
  },

  async get(workspaceId: string, id: string): Promise<WorkflowDefinitionResponse> {
    const res = await http.get<ApiEnvelope<WorkflowDefinitionResponse>>(`${base(workspaceId)}/${id}`);
    return unwrap(res);
  },

  async update(workspaceId: string, id: string, payload: UpdateWorkflowDefinitionRequest): Promise<WorkflowDefinitionResponse> {
    const res = await http.put<ApiEnvelope<WorkflowDefinitionResponse>>(`${base(workspaceId)}/${id}`, payload);
    return unwrap(res);
  },

  async remove(workspaceId: string, id: string): Promise<void> {
    await http.delete(`${base(workspaceId)}/${id}`);
  },

  async versions(workspaceId: string, id: string): Promise<WorkflowVersionResponse[]> {
    const res = await http.get<ApiEnvelope<WorkflowVersionResponse[]>>(`${base(workspaceId)}/${id}/versions`);
    return unwrap(res);
  },

  async publish(workspaceId: string, id: string): Promise<WorkflowVersionResponse> {
    const res = await http.post<ApiEnvelope<WorkflowVersionResponse>>(`${base(workspaceId)}/${id}/publish`, {});
    return unwrap(res);
  },

  async history(workspaceId: string, id: string): Promise<WorkflowCommit[]> {
    const res = await http.get<ApiEnvelope<WorkflowCommit[]>>(`${base(workspaceId)}/${id}/history`);
    return unwrap(res);
  },

  async diff(workspaceId: string, id: string, from: string, to: string): Promise<WorkflowDiff> {
    const res = await http.get<ApiEnvelope<WorkflowDiff>>(`${base(workspaceId)}/${id}/diff`, { params: { from, to } });
    return unwrap(res);
  },

  async at(workspaceId: string, id: string, commitSha: string): Promise<WorkflowAtCommitResponse> {
    const res = await http.get<ApiEnvelope<WorkflowAtCommitResponse>>(`${base(workspaceId)}/${id}/at/${commitSha}`);
    return unwrap(res);
  },

  async suggestClone(workspaceId: string, partialName: string): Promise<{ id: string; name: string; similarityScore: number } | null> {
    try {
      const res = await http.get<ApiEnvelope<{ id: string; name: string; similarityScore: number } | null>>(
        `${base(workspaceId)}/suggest-clone`,
        { params: { name: partialName } },
      );
      return unwrap(res);
    } catch {
      return null;
    }
  },

  async clone(workspaceId: string, id: string): Promise<{ id: string; name: string; slug: string }> {
    const res = await http.post<ApiEnvelope<{ id: string; name: string; slug: string }>>(
      `${base(workspaceId)}/${id}/clone`,
      {},
    );
    return unwrap(res);
  },

  async validate(workspaceId: string, yamlContent: string): Promise<{ errors: Array<{ message: string; line?: number }>; warnings: Array<{ message: string; line?: number }> }> {
    const res = await http.post<ApiEnvelope<{ errors: Array<{ message: string; line?: number }>; warnings: Array<{ message: string; line?: number }> }>>(
      `${base(workspaceId)}/validate`,
      { yamlContent },
    );
    return unwrap(res);
  },
};
