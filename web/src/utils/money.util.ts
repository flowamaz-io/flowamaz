import {
  MAX_PRICE_CENTS,
  MIN_PRICE_CENTS,
  SUPPORTED_CURRENCIES,
  type ListingPricingRequest,
  type PricingDraft,
  type PricingModel,
  type SupportedCurrency,
} from '@/types/marketplace';

/** Number of minor-unit digits for a currency (2 for all currently supported currencies). */
export function minorDigits(currency: string): number {
  try {
    const digits = new Intl.NumberFormat('en', {
      style: 'currency',
      currency: currency.toUpperCase(),
    }).resolvedOptions().maximumFractionDigits;
    return digits ?? 2;
  } catch {
    return 2;
  }
}

const NARROW_SYMBOL_CURRENCIES = new Set(['USD', 'EUR', 'GBP', 'INR']);

/**
 * Formats integer minor units as a localised currency string. Whole amounts drop the
 * decimals ("$49"), fractional amounts keep them ("$19.99").
 */
export function formatMoney(minor: number, currency: string, locale?: string): string {
  const digits = minorDigits(currency);
  const factor = 10 ** digits;
  const major = minor / factor;
  const whole = Number.isInteger(major);
  const code = currency.toUpperCase();
  try {
    return new Intl.NumberFormat(locale, {
      style: 'currency',
      currency: code,
      // Locales outside the US render USD as "US$49"; the narrow symbol keeps "$49" everywhere.
      // Only for currencies whose narrow symbol is unambiguous — SGD would also become "$".
      currencyDisplay: NARROW_SYMBOL_CURRENCIES.has(code) ? 'narrowSymbol' : 'symbol',
      minimumFractionDigits: whole ? 0 : digits,
      maximumFractionDigits: digits,
    }).format(major);
  } catch {
    // Unknown currency code from the API — fall back to a readable plain format.
    return `${major.toFixed(whole ? 0 : digits)} ${currency.toUpperCase()}`;
  }
}

/** Price badge text: "Free", "$49" (one-time) or "$19/mo" (monthly). */
export function formatPrice(
  pricingModel: PricingModel | undefined,
  priceCents: number | undefined,
  currency: string | undefined,
  locale?: string,
): string {
  if (!pricingModel || pricingModel === 'free' || !priceCents || !currency) return 'Free';
  const amount = formatMoney(priceCents, currency, locale);
  return pricingModel === 'monthly' ? `${amount}/mo` : amount;
}

/** True when a template is paid (and therefore may require a purchase). */
export function isPaid(pricingModel: PricingModel | undefined, priceCents: number | undefined): boolean {
  return !!pricingModel && pricingModel !== 'free' && (priceCents ?? 0) > 0;
}

/**
 * Parses a major-unit amount typed by a user ("49", "19.99", "1,250.5") into integer minor
 * units without floating-point drift. Returns null when the input isn't a valid amount.
 */
export function majorToMinor(input: string, currency: string = 'usd'): number | null {
  const digits = minorDigits(currency);
  const cleaned = input.trim().replace(/,/g, '');
  if (cleaned.length === 0) return null;
  const pattern = digits > 0 ? new RegExp(`^\\d+(\\.\\d{0,${digits}})?$`) : /^\d+$/;
  if (!pattern.test(cleaned)) return null;
  const [whole, fraction = ''] = cleaned.split('.');
  const padded = (fraction + '0'.repeat(digits)).slice(0, digits);
  return Number(whole) * 10 ** digits + (digits > 0 ? Number(padded) : 0);
}

/** Converts minor units to an editable major-unit string ("4900" → "49", "1999" → "19.99"). */
export function minorToMajorInput(minor: number, currency: string = 'usd'): string {
  const digits = minorDigits(currency);
  const major = minor / 10 ** digits;
  return Number.isInteger(major) ? String(major) : major.toFixed(digits);
}

/** Flowamaz platform fee on a price, in minor units (rounded to the nearest minor unit). */
export function platformFee(priceCents: number, feePercent: number): number {
  if (priceCents <= 0 || feePercent <= 0) return 0;
  return Math.round((priceCents * feePercent) / 100);
}

/** What the publisher receives after the platform fee, in minor units. */
export function publisherNet(priceCents: number, feePercent: number): number {
  return Math.max(0, priceCents - platformFee(priceCents, feePercent));
}

export function isSupportedCurrency(value: string): value is SupportedCurrency {
  return (SUPPORTED_CURRENCIES as readonly string[]).includes(value.toLowerCase());
}

/**
 * Validates a pricing draft. Returns an actionable error message, or null when valid.
 * Free drafts are always valid.
 */
export function validatePricingDraft(draft: PricingDraft): string | null {
  if (draft.pricingModel === 'free') return null;
  if (!isSupportedCurrency(draft.currency)) {
    return `Pick one of the supported currencies: ${SUPPORTED_CURRENCIES.map((c) => c.toUpperCase()).join(', ')}.`;
  }
  const cents = majorToMinor(draft.amount, draft.currency);
  if (cents === null) {
    return 'Enter the price as a number, e.g. 49 or 19.99.';
  }
  if (cents < MIN_PRICE_CENTS || cents > MAX_PRICE_CENTS) {
    return `Set a price between ${formatMoney(MIN_PRICE_CENTS, draft.currency)} and ${formatMoney(MAX_PRICE_CENTS, draft.currency)}.`;
  }
  return null;
}

/** Converts a (validated) draft into the API pricing payload. */
export function draftToPricing(draft: PricingDraft): ListingPricingRequest {
  if (draft.pricingModel === 'free') {
    return { pricingModel: 'free', priceCents: 0, currency: draft.currency };
  }
  return {
    pricingModel: draft.pricingModel,
    priceCents: majorToMinor(draft.amount, draft.currency) ?? 0,
    currency: draft.currency,
  };
}

/** Builds an editable draft from stored pricing. */
export function pricingToDraft(pricing: {
  pricingModel: PricingModel;
  priceCents: number;
  currency: string;
}): PricingDraft {
  const currency: SupportedCurrency = isSupportedCurrency(pricing.currency)
    ? (pricing.currency.toLowerCase() as SupportedCurrency)
    : 'usd';
  return {
    pricingModel: pricing.pricingModel,
    amount: pricing.pricingModel === 'free' || pricing.priceCents <= 0 ? '' : minorToMajorInput(pricing.priceCents, currency),
    currency,
  };
}

export const PRICING_MODEL_LABELS: Record<PricingModel, string> = {
  free: 'Free',
  one_time: 'One-time',
  monthly: 'Monthly',
};
