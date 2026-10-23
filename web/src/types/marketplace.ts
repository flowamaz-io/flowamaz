// Workflow Economy DTOs (FUNCTIONAL.md §8.9) — paid workflow templates, purchases and the
// publisher (Stripe Connect Express) surface. Money is ALWAYS integer minor units (cents).

export type PricingModel = 'free' | 'one_time' | 'monthly';

/** Lowercase ISO-4217 codes accepted by the backend for paid listings. */
export const SUPPORTED_CURRENCIES = ['usd', 'eur', 'gbp', 'myr', 'sgd', 'inr'] as const;
export type SupportedCurrency = (typeof SUPPORTED_CURRENCIES)[number];

/** Min / max price for a paid listing, in minor units. */
export const MIN_PRICE_CENTS = 100;
export const MAX_PRICE_CENTS = 1_000_000;

/** Pricing fields added to template list items and template detail. */
export interface TemplatePricing {
  pricingModel: PricingModel;
  priceCents: number;
  currency: string;
  publisherName: string | null;
}

export type PurchaseStatus = 'none' | 'pending' | 'active' | 'canceled' | 'past_due' | 'refunded';

/** GET /workspaces/{ws}/templates/{templateId}/access */
export interface TemplateAccess {
  requiresPurchase: boolean;
  owned: boolean;
  status: PurchaseStatus;
  currentPeriodEnd: string | null;
  cancelAtPeriodEnd: boolean;
}

/** Body for POST /workspaces/{ws}/templates/{templateId}/purchase */
export interface PurchaseTemplateRequest {
  successUrl: string;
  cancelUrl: string;
}

/** GET /workspaces/{ws}/purchases item. */
export interface TemplatePurchase {
  id: string;
  templateId: string;
  templateName: string;
  pricingModel: PricingModel;
  amountCents: number;
  currency: string;
  status: PurchaseStatus;
  purchasedAt: string;
  currentPeriodEnd: string | null;
  cancelAtPeriodEnd: boolean;
}

/** GET /publisher */
export interface PublisherStatus {
  connected: boolean;
  chargesEnabled: boolean;
  payoutsEnabled: boolean;
  detailsSubmitted: boolean;
  requirementsDue: string[];
  country: string | null;
  canSellPaidWorkflows: boolean;
  platformFeePercent: number;
}

/** Body for POST /publisher/onboarding */
export interface PublisherOnboardingRequest {
  returnUrl: string;
  refreshUrl: string;
}

export interface PublisherListing {
  templateId: string;
  name: string;
  pricingModel: PricingModel;
  priceCents: number;
  currency: string;
  reviewStatus: string;
  installCount: number;
  salesCount: number;
  netCents: number;
}

export type LedgerEntryType = 'sale' | 'renewal' | 'refund';

export interface PublisherLedgerEntry {
  id: string;
  templateId: string;
  templateName: string;
  type: LedgerEntryType;
  grossCents: number;
  feeCents: number;
  netCents: number;
  currency: string;
  occurredAt: string;
}

/** GET /publisher/earnings */
export interface PublisherEarnings {
  currency: string;
  grossCents: number;
  feeCents: number;
  netCents: number;
  salesCount: number;
  activeSubscriptions: number;
  listings: PublisherListing[];
  entries: PublisherLedgerEntry[];
}

/** Body for PUT /publisher/listings/{templateId}/pricing (and the optional publish fields). */
export interface ListingPricingRequest {
  pricingModel: PricingModel;
  priceCents: number;
  currency: string;
}

/** Editable pricing form state — price is typed in MAJOR units as a string. */
export interface PricingDraft {
  pricingModel: PricingModel;
  amount: string;
  currency: SupportedCurrency;
}

/** Derived onboarding state for the publisher status card. */
export type PublisherOnboardingState = 'not_connected' | 'incomplete' | 'requirements_due' | 'ready';
