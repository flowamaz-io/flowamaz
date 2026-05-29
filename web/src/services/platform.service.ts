import { http, unwrap } from './api.service';
import type { ApiEnvelope, UsageResponse } from '@/types';

export const platformService = {
  async usage(workspaceId: string): Promise<UsageResponse> {
    const res = await http.get<ApiEnvelope<UsageResponse>>(`/api/v1/workspaces/${workspaceId}/usage`);
    return unwrap(res);
  },
};
