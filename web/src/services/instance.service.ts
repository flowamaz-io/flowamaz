import { http, unwrap } from './api.service';
import type {
  ApiEnvelope,
  EventResponse,
  GateResponse,
  InstanceDetailResponse,
  InstanceListItem,
  InterpreterNarrative,
  NarrativeAudience,
  PagedResult,
  TimelineResponse,
  TriggerInstanceRequest,
  TriggerInstanceResponse,
  VariableResponse,
} from '@/types';

const instances = (workspaceId: string): string => `/api/v1/workspaces/${workspaceId}/instances`;
const gates = (workspaceId: string): string => `/api/v1/workspaces/${workspaceId}/gates`;

export interface InstanceListFilters {
  status?: string;
  workflowDefinitionId?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export const instanceService = {
  async list(workspaceId: string, filters: InstanceListFilters = {}): Promise<PagedResult<InstanceListItem>> {
    const res = await http.get<ApiEnvelope<PagedResult<InstanceListItem>>>(instances(workspaceId), {
      params: { page: 1, pageSize: 20, ...filters },
    });
    return unwrap(res);
  },

  async trigger(workspaceId: string, payload: TriggerInstanceRequest): Promise<TriggerInstanceResponse> {
    const res = await http.post<ApiEnvelope<TriggerInstanceResponse>>(instances(workspaceId), payload);
    return unwrap(res);
  },

  async get(workspaceId: string, id: string): Promise<InstanceDetailResponse> {
    const res = await http.get<ApiEnvelope<InstanceDetailResponse>>(`${instances(workspaceId)}/${id}`);
    return unwrap(res);
  },

  async cancel(workspaceId: string, id: string): Promise<InstanceDetailResponse> {
    const res = await http.post<ApiEnvelope<InstanceDetailResponse>>(`${instances(workspaceId)}/${id}/cancel`, {});
    return unwrap(res);
  },

  async retry(workspaceId: string, id: string): Promise<TriggerInstanceResponse> {
    const res = await http.post<ApiEnvelope<TriggerInstanceResponse>>(`${instances(workspaceId)}/${id}/retry`, {});
    return unwrap(res);
  },

  async events(workspaceId: string, id: string, page = 1, pageSize = 50): Promise<PagedResult<EventResponse>> {
    const res = await http.get<ApiEnvelope<PagedResult<EventResponse>>>(`${instances(workspaceId)}/${id}/events`, {
      params: { page, pageSize },
    });
    return unwrap(res);
  },

  async variables(workspaceId: string, id: string): Promise<VariableResponse[]> {
    const res = await http.get<ApiEnvelope<VariableResponse[]>>(`${instances(workspaceId)}/${id}/variables`);
    return unwrap(res);
  },

  async timeline(workspaceId: string, id: string): Promise<TimelineResponse> {
    const res = await http.get<ApiEnvelope<TimelineResponse>>(`${instances(workspaceId)}/${id}/timeline`);
    return unwrap(res);
  },

  async narrative(workspaceId: string, id: string, audience: NarrativeAudience): Promise<InterpreterNarrative> {
    const res = await http.get<ApiEnvelope<InterpreterNarrative>>(`${instances(workspaceId)}/${id}/narrative`, {
      params: { audience },
    });
    return unwrap(res);
  },

  async downloadNarrative(workspaceId: string, id: string, audience: NarrativeAudience): Promise<Blob> {
    const res = await http.post(`${instances(workspaceId)}/${id}/narrative/download`, {}, {
      params: { audience },
      responseType: 'blob',
    });
    return res.data as Blob;
  },

  // ── Gates ──────────────────────────────────────────────────────────────────
  async listGates(workspaceId: string): Promise<GateResponse[]> {
    const res = await http.get<ApiEnvelope<GateResponse[]>>(gates(workspaceId));
    return unwrap(res);
  },

  async decideGate(
    workspaceId: string,
    instanceId: string,
    nodeId: string,
    decision: 'approved' | 'rejected',
    note?: string,
  ): Promise<GateResponse> {
    const res = await http.post<ApiEnvelope<GateResponse>>(
      `${gates(workspaceId)}/${instanceId}/${nodeId}/decide`,
      { decision, note },
    );
    return unwrap(res);
  },
};
