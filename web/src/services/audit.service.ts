import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';

export interface AuditRow {
  id: string;
  timestamp: string;
  actorType: string;
  actor: string | null;
  actorUserId: string | null;
  eventType: string;
  action: string;
  resourceType: string;
  resourceId: string | null;
  resource: string | null;
  ipAddress: string | null;
  userAgent: string | null;
  summary: string;
  metadata: string | null;
}

export interface AuditPagination {
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
}

export interface AuditPage {
  data: AuditRow[];
  pagination: AuditPagination;
}

export interface AuditQuery {
  from?: string;
  to?: string;
  eventType?: string;
  actorId?: string;
  resourceType?: string;
  page?: number;
  pageSize?: number;
}

function toParams(query: AuditQuery): Record<string, string | number> {
  const params: Record<string, string | number> = {};
  if (query.from) params.from = query.from;
  if (query.to) params.to = query.to;
  if (query.eventType) params.event_type = query.eventType;
  if (query.actorId) params.actor_id = query.actorId;
  if (query.resourceType) params.resource_type = query.resourceType;
  if (query.page) params.page = query.page;
  if (query.pageSize) params.page_size = query.pageSize;
  return params;
}

export const auditService = {
  async list(workspaceId: string, query: AuditQuery): Promise<AuditPage> {
    const res = await http.get<ApiEnvelope<AuditPage>>(
      `/api/v1/workspaces/${workspaceId}/audit`,
      { params: toParams(query) },
    );
    return unwrap(res);
  },

  /** Downloads a CSV export of the workspace audit log for the given range. */
  async exportCsv(workspaceId: string, query: AuditQuery): Promise<Blob> {
    const res = await http.get(`/api/v1/workspaces/${workspaceId}/audit/export`, {
      params: toParams(query),
      responseType: 'blob',
    });
    return res.data as Blob;
  },
};
