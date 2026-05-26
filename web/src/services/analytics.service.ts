import { http, unwrap } from './api.service';
import type { ApiEnvelope, InsightResponse, PagedResult, WeatherResponse } from '@/types';

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
};
