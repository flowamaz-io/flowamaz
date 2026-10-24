import { http, unwrap } from './api.service';
import type { ApiEnvelope, GateResponse } from '@/types';

const gatesBase = (workspaceId: string): string =>
  `/api/v1/workspaces/${workspaceId}/gates`;

export const gateService = {
  async getPendingGates(workspaceId: string): Promise<GateResponse[]> {
    const res = await http.get<ApiEnvelope<GateResponse[]>>(gatesBase(workspaceId));
    return unwrap(res);
  },

  /** Decided gates plus the actionable pending ones, newest first — for the "All" tab. */
  async getAllGates(workspaceId: string): Promise<GateResponse[]> {
    const res = await http.get<ApiEnvelope<GateResponse[]>>(gatesBase(workspaceId), { params: { status: 'all' } });
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
      `${gatesBase(workspaceId)}/${instanceId}/${nodeId}/decide`,
      { decision, note },
    );
    return unwrap(res);
  },
};
