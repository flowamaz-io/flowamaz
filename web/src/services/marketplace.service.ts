import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';
import type { PurchaseTemplateRequest, TemplateAccess, TemplatePurchase } from '@/types/marketplace';

/** Workflow Economy — buyer side (template access, Checkout, workspace purchases). */
export const marketplaceService = {
  async access(workspaceId: string, templateId: string): Promise<TemplateAccess> {
    const res = await http.get<ApiEnvelope<TemplateAccess>>(
      `/api/v1/workspaces/${workspaceId}/templates/${templateId}/access`,
    );
    return unwrap(res);
  },

  /** Returns the Stripe Checkout URL to redirect the browser to. Org owner only (403 otherwise). */
  async purchase(workspaceId: string, templateId: string, payload: PurchaseTemplateRequest): Promise<string> {
    const res = await http.post<ApiEnvelope<{ url: string }>>(
      `/api/v1/workspaces/${workspaceId}/templates/${templateId}/purchase`,
      payload,
    );
    return unwrap(res).url;
  },

  async listPurchases(workspaceId: string): Promise<TemplatePurchase[]> {
    const res = await http.get<ApiEnvelope<TemplatePurchase[]>>(`/api/v1/workspaces/${workspaceId}/purchases`);
    return unwrap(res);
  },

  /** Monthly only — cancels at the end of the current period. */
  async cancelPurchase(workspaceId: string, purchaseId: string): Promise<TemplatePurchase> {
    const res = await http.post<ApiEnvelope<TemplatePurchase>>(
      `/api/v1/workspaces/${workspaceId}/purchases/${purchaseId}/cancel`,
    );
    return unwrap(res);
  },
};
