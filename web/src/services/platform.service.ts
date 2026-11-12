import { http, unwrap } from './api.service';
import type { ApiEnvelope, UsageResponse } from '@/types';

/** An organisation with its public API limit (Flowamaz staff screen). */
export interface PlatformOrgApiLimit {
  orgId: string;
  name: string;
  slug: string;
  planName: string;
  /** What the plan allows per minute. */
  planPerMinute: number;
  /** Agreed for this organisation; null when the plan's limit applies. 0 = no per-minute limit. */
  customPerMinute: number | null;
  /** The limit in force. */
  effectivePerMinute: number;
}

export const platformService = {
  async usage(workspaceId: string): Promise<UsageResponse> {
    const res = await http.get<ApiEnvelope<UsageResponse>>(`/api/v1/workspaces/${workspaceId}/usage`);
    return unwrap(res);
  },

  async searchOrganisations(search?: string): Promise<PlatformOrgApiLimit[]> {
    const res = await http.get<ApiEnvelope<PlatformOrgApiLimit[]>>('/api/v1/platform/organisations', {
      params: search ? { search } : undefined,
    });
    return unwrap(res);
  },

  /** null returns the organisation to its plan's limit. */
  async setApiLimit(orgId: string, perMinute: number | null): Promise<PlatformOrgApiLimit> {
    const res = await http.put<ApiEnvelope<PlatformOrgApiLimit>>(`/api/v1/platform/organisations/${orgId}/api-limit`, { perMinute });
    return unwrap(res);
  },
};
