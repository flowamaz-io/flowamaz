import { http, unwrap } from './api.service';
import type {
  ApiEnvelope,
  InsightResponse,
  PagedResult,
  RoiConfigDto,
  WeatherResponse,
  WorkspaceRoiSummary,
} from '@/types';

export const analyticsService = {
  async weather(workspaceId: string): Promise<WeatherResponse> {
    const res = await http.get<ApiEnvelope<WeatherResponse>>(`/api/v1/workspaces/${workspaceId}/weather`);
    return unwrap(res);
  },

  async insights(
    workspaceId: string,
    filters: { severity?: string; acknowledged?: boolean; workflowDefinitionId?: string } = {},
  ): Promise<PagedResult<InsightResponse>> {
    const res = await http.get<ApiEnvelope<PagedResult<InsightResponse>>>(
      `/api/v1/workspaces/${workspaceId}/insights`,
      { params: filters },
    );
    return unwrap(res);
  },

  async acknowledge(workspaceId: string, id: string): Promise<void> {
    await http.post(`/api/v1/workspaces/${workspaceId}/insights/${id}/acknowledge`, {});
  },

  async roi(workspaceId: string, from?: string, to?: string): Promise<WorkspaceRoiSummary> {
    const res = await http.get<ApiEnvelope<WorkspaceRoiSummary>>(
      `/api/v1/workspaces/${workspaceId}/analytics/roi`,
      { params: { from, to } },
    );
    return unwrap(res);
  },

  async getRoiConfig(workspaceId: string, workflowId: string): Promise<RoiConfigDto | null> {
    try {
      const res = await http.get<ApiEnvelope<RoiConfigDto>>(
        `/api/v1/workspaces/${workspaceId}/workflows/${workflowId}/roi-config`,
      );
      return unwrap(res);
    } catch {
      return null; // 404 = not configured yet
    }
  },

  async saveRoiConfig(workspaceId: string, workflowId: string, config: RoiConfigDto): Promise<RoiConfigDto> {
    const res = await http.put<ApiEnvelope<RoiConfigDto>>(
      `/api/v1/workspaces/${workspaceId}/workflows/${workflowId}/roi-config`,
      config,
    );
    return unwrap(res);
  },
};
