import { http, unwrap } from './api.service';
import type {
  ApiEnvelope,
  CreateWorkflowDefinitionRequest,
  PagedResult,
  UpdateWorkflowDefinitionRequest,
  WorkflowDefinitionListItem,
  WorkflowDefinitionResponse,
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
};
