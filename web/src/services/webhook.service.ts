import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';

export interface WebhookEndpoint {
  id: string;
  workflowDefinitionId: string;
  description: string | null;
  isActive: boolean;
  allowedIps: string[];
  createdAt: string;
}

/** Returned once on create/rotate — the secret is never shown again. */
export interface CreatedWebhook {
  endpointId: string;
  secret: string;
}

export interface CreateWebhookRequest {
  workflowDefinitionId: string;
  description?: string | null;
}

const base = (workspaceId: string): string => `/api/v1/workspaces/${workspaceId}/webhooks`;

export const webhookService = {
  async list(workspaceId: string): Promise<WebhookEndpoint[]> {
    const res = await http.get<ApiEnvelope<WebhookEndpoint[]>>(base(workspaceId));
    return unwrap(res);
  },

  async listForWorkflow(workspaceId: string, workflowId: string): Promise<WebhookEndpoint[]> {
    const res = await http.get<ApiEnvelope<WebhookEndpoint[]>>(base(workspaceId), {
      params: { workflowId },
    });
    return unwrap(res);
  },

  async create(workspaceId: string, payload: CreateWebhookRequest): Promise<CreatedWebhook> {
    const res = await http.post<ApiEnvelope<CreatedWebhook>>(base(workspaceId), payload);
    return unwrap(res);
  },

  async remove(workspaceId: string, endpointId: string): Promise<void> {
    await http.delete(`${base(workspaceId)}/${endpointId}`);
  },

  async rotate(workspaceId: string, endpointId: string): Promise<CreatedWebhook> {
    const res = await http.post<ApiEnvelope<CreatedWebhook>>(`${base(workspaceId)}/${endpointId}/rotate`, {});
    return unwrap(res);
  },
};

/** Public receive URL for an endpoint, shown in the UI and curl examples. */
export function webhookUrl(endpointId: string): string {
  return `https://app.flowamaz.io/webhooks/${endpointId}`;
}
