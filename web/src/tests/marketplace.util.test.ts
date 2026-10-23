import { describe, expect, it } from 'vitest';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import {
  canCancelPurchase,
  humanizeRequirement,
  isForbiddenError,
  isPublisherNotReadyError,
  isPurchaseRequiredError,
  isStripeTestMode,
  publisherOnboardingState,
} from '@/utils/marketplace.util';
import type { PublisherStatus, TemplatePurchase } from '@/types/marketplace';

function httpError(status: number, code?: string): AxiosError {
  const response = {
    status,
    statusText: '',
    headers: {},
    config: { headers: new AxiosHeaders() },
    data: { success: false, statusCode: status, code, message: 'x', correlationId: 'c' },
  } as AxiosResponse;
  return new AxiosError('fail', 'ERR_BAD_REQUEST', undefined, undefined, response);
}

const baseStatus: PublisherStatus = {
  connected: true,
  chargesEnabled: true,
  payoutsEnabled: true,
  detailsSubmitted: true,
  requirementsDue: [],
  country: 'MY',
  canSellPaidWorkflows: true,
  platformFeePercent: 20,
};

const basePurchase: TemplatePurchase = {
  id: 'p1',
  templateId: 't1',
  templateName: 'Invoice approval',
  pricingModel: 'monthly',
  amountCents: 1900,
  currency: 'usd',
  status: 'active',
  purchasedAt: '2026-10-01T00:00:00Z',
  currentPeriodEnd: '2026-11-01T00:00:00Z',
  cancelAtPeriodEnd: false,
};

describe('API error classification', () => {
  it('detects TEMPLATE_PURCHASE_REQUIRED by code or by HTTP 402', () => {
    expect(isPurchaseRequiredError(httpError(402, 'TEMPLATE_PURCHASE_REQUIRED'))).toBe(true);
    expect(isPurchaseRequiredError(httpError(402))).toBe(true);
    expect(isPurchaseRequiredError(httpError(400, 'VALIDATION'))).toBe(false);
    expect(isPurchaseRequiredError(new Error('boom'))).toBe(false);
  });

  it('detects PUBLISHER_NOT_READY and 403', () => {
    expect(isPublisherNotReadyError(httpError(422, 'PUBLISHER_NOT_READY'))).toBe(true);
    expect(isPublisherNotReadyError(httpError(422, 'VALIDATION'))).toBe(false);
    expect(isForbiddenError(httpError(403))).toBe(true);
    expect(isForbiddenError(httpError(401))).toBe(false);
  });
});

describe('publisherOnboardingState', () => {
  it('maps the Stripe account flags to a card state', () => {
    expect(publisherOnboardingState({ ...baseStatus, connected: false })).toBe('not_connected');
    expect(publisherOnboardingState(baseStatus)).toBe('ready');
    expect(publisherOnboardingState({ ...baseStatus, chargesEnabled: false })).toBe('incomplete');
    expect(
      publisherOnboardingState({ ...baseStatus, chargesEnabled: false, requirementsDue: ['external_account'] }),
    ).toBe('requirements_due');
    expect(publisherOnboardingState({ ...baseStatus, requirementsDue: ['individual.dob.day'] })).toBe(
      'requirements_due',
    );
  });
});

describe('humanizeRequirement', () => {
  it('turns Stripe requirement keys into readable text', () => {
    expect(humanizeRequirement('individual.verification.document')).toBe('Individual — verification — document');
    expect(humanizeRequirement('external_account')).toBe('External account');
  });
});

describe('canCancelPurchase', () => {
  it('allows cancelling running monthly subscriptions only', () => {
    expect(canCancelPurchase(basePurchase)).toBe(true);
    expect(canCancelPurchase({ ...basePurchase, status: 'past_due' })).toBe(true);
    expect(canCancelPurchase({ ...basePurchase, cancelAtPeriodEnd: true })).toBe(false);
    expect(canCancelPurchase({ ...basePurchase, status: 'canceled' })).toBe(false);
    expect(canCancelPurchase({ ...basePurchase, pricingModel: 'one_time' })).toBe(false);
  });
});

describe('isStripeTestMode', () => {
  it('is on for every mode except production', () => {
    expect(isStripeTestMode('development')).toBe(true);
    expect(isStripeTestMode('test')).toBe(true);
    expect(isStripeTestMode('production')).toBe(false);
  });
});
