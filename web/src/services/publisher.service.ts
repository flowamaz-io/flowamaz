import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';
import type {
  ListingPricingRequest,
  PublisherEarnings,
  PublisherListing,
  PublisherOnboardingRequest,
  PublisherStatus,
} from '@/types/marketplace';

/** Workflow Economy — publisher side (Stripe Connect Express). All endpoints are org-owner only. */
export const publisherService = {
  async status(): Promise<PublisherStatus> {
    const res = await http.get<ApiEnvelope<PublisherStatus>>('/api/v1/publisher');
    return unwrap(res);
  },

  /** Returns the Stripe Connect onboarding URL to redirect the browser to. */
  async onboarding(payload: PublisherOnboardingRequest): Promise<string> {
    const res = await http.post<ApiEnvelope<{ url: string }>>('/api/v1/publisher/onboarding', payload);
    return unwrap(res).url;
  },

  /** Returns a one-time Stripe Express dashboard URL (payouts) — open it in a new tab. */
  async dashboardLink(): Promise<string> {
    const res = await http.post<ApiEnvelope<{ url: string }>>('/api/v1/publisher/dashboard-link');
    return unwrap(res).url;
  },

  async earnings(): Promise<PublisherEarnings> {
    const res = await http.get<ApiEnvelope<PublisherEarnings>>('/api/v1/publisher/earnings');
    return unwrap(res);
  },

  async updateListingPricing(templateId: string, payload: ListingPricingRequest): Promise<PublisherListing> {
    const res = await http.put<ApiEnvelope<PublisherListing>>(
      `/api/v1/publisher/listings/${templateId}/pricing`,
      payload,
    );
    return unwrap(res);
  },
};
