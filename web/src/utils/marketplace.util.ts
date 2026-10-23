import axios from 'axios';
import type { ApiErrorEnvelope } from '@/types';
import type {
  PublisherOnboardingState,
  PublisherStatus,
  PurchaseStatus,
  TemplatePurchase,
} from '@/types/marketplace';

export const TEMPLATE_PURCHASE_REQUIRED = 'TEMPLATE_PURCHASE_REQUIRED';
export const PUBLISHER_NOT_READY = 'PUBLISHER_NOT_READY';

/** HTTP status + API error code of a thrown request error (both undefined for non-HTTP errors). */
export function apiErrorInfo(error: unknown): { status?: number; code?: string } {
  if (!axios.isAxiosError(error)) return {};
  const data = error.response?.data as Partial<ApiErrorEnvelope> | undefined;
  return { status: error.response?.status, code: typeof data?.code === 'string' ? data.code : undefined };
}

/** Install refused because the paid template isn't owned (HTTP 402 TEMPLATE_PURCHASE_REQUIRED). */
export function isPurchaseRequiredError(error: unknown): boolean {
  const { status, code } = apiErrorInfo(error);
  return code === TEMPLATE_PURCHASE_REQUIRED || status === 402;
}

/** Paid publish refused because the publisher account can't take charges yet (422 PUBLISHER_NOT_READY). */
export function isPublisherNotReadyError(error: unknown): boolean {
  return apiErrorInfo(error).code === PUBLISHER_NOT_READY;
}

export function isForbiddenError(error: unknown): boolean {
  return apiErrorInfo(error).status === 403;
}

export const OWNER_ONLY_PURCHASE_MESSAGE =
  'Only your organisation owner can buy paid templates. Ask your org owner to open this template and complete the purchase — it unlocks for this workspace.';

export const OWNER_ONLY_PUBLISHER_MESSAGE =
  'Only your organisation owner can set up publisher payouts and sell paid workflows. Ask your org owner to open Settings → Publisher.';

/** Derives which onboarding step the publisher status card should show. */
export function publisherOnboardingState(status: PublisherStatus): PublisherOnboardingState {
  if (!status.connected) return 'not_connected';
  if (status.chargesEnabled && status.payoutsEnabled && status.requirementsDue.length === 0) return 'ready';
  if (status.requirementsDue.length > 0) return 'requirements_due';
  if (status.chargesEnabled && status.payoutsEnabled) return 'ready';
  return 'incomplete';
}

/** Turns a Stripe requirement key ("individual.verification.document") into readable text. */
export function humanizeRequirement(key: string): string {
  const parts = key.split('.').filter((p) => p.length > 0);
  const text = parts
    .map((p) => p.replace(/_/g, ' '))
    .join(' — ');
  return text.length > 0 ? text.charAt(0).toUpperCase() + text.slice(1) : key;
}

/** A monthly subscription that is still running and not already set to end can be cancelled. */
export function canCancelPurchase(purchase: TemplatePurchase): boolean {
  return (
    purchase.pricingModel === 'monthly' &&
    (purchase.status === 'active' || purchase.status === 'past_due') &&
    !purchase.cancelAtPeriodEnd
  );
}

export const PURCHASE_STATUS_LABELS: Record<PurchaseStatus, string> = {
  none: 'Not purchased',
  pending: 'Processing',
  active: 'Active',
  canceled: 'Canceled',
  past_due: 'Payment failed',
  refunded: 'Refunded',
};

export const PURCHASE_STATUS_VARIANT: Record<PurchaseStatus, 'primary' | 'amber' | 'danger' | 'slate' | 'blue'> = {
  none: 'slate',
  pending: 'blue',
  active: 'primary',
  canceled: 'slate',
  past_due: 'danger',
  refunded: 'amber',
};

/** Stripe test mode hint is shown everywhere except production builds. */
export function isStripeTestMode(mode: string = import.meta.env.MODE): boolean {
  return mode !== 'production';
}
