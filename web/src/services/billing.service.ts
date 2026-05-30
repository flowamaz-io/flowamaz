import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';

export interface BillingPlan {
  id: string;
  name: string;
  slug: string;
  priceMonthlyUsd: number;
  priceAnnualUsd: number;
  maxWorkflows: number;
  maxRunsMonth: number;
  maxMembers: number;
  maxWorkspaces: number;
  features: string[];
}

export interface CurrentSubscription {
  planId: string;
  planName: string;
  planSlug: string;
  billingCycle: string;
  status: string;
  trialEndsAt: string | null;
  trialDaysRemaining: number | null;
  currentPeriodEnd: string | null;
  paymentFailed: boolean;
  hasBillingAccount: boolean;
}

export interface BillingUsage {
  period: string;
  runsUsed: number;
  runsLimit: number;
  aiCallsUsed: number;
  aiCostUsd: number;
  membersUsed: number;
  membersLimit: number;
  workflowsLimit: number;
}

export interface CheckoutRequest {
  planId: string;
  annual: boolean;
  successUrl: string;
  cancelUrl: string;
}

export const billingService = {
  async plans(): Promise<BillingPlan[]> {
    const res = await http.get<ApiEnvelope<BillingPlan[]>>('/api/v1/billing/plans');
    return unwrap(res);
  },

  async current(): Promise<CurrentSubscription> {
    const res = await http.get<ApiEnvelope<CurrentSubscription>>('/api/v1/billing/current');
    return unwrap(res);
  },

  async usage(): Promise<BillingUsage> {
    const res = await http.get<ApiEnvelope<BillingUsage>>('/api/v1/billing/usage');
    return unwrap(res);
  },

  /** Returns the Stripe Checkout URL to redirect the browser to. */
  async checkout(payload: CheckoutRequest): Promise<string> {
    const res = await http.post<ApiEnvelope<{ url: string }>>('/api/v1/billing/checkout', payload);
    return unwrap(res).url;
  },

  /** Returns the Stripe Customer Portal URL to redirect the browser to. */
  async portal(returnUrl: string): Promise<string> {
    const res = await http.post<ApiEnvelope<{ url: string }>>('/api/v1/billing/portal', { returnUrl });
    return unwrap(res).url;
  },
};
