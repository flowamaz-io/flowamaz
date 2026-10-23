import { describe, expect, it } from 'vitest';
import {
  draftToPricing,
  formatMoney,
  formatPrice,
  isPaid,
  majorToMinor,
  minorToMajorInput,
  platformFee,
  pricingToDraft,
  publisherNet,
  validatePricingDraft,
} from '@/utils/money.util';

describe('formatMoney', () => {
  it('drops decimals for whole amounts', () => {
    expect(formatMoney(4900, 'usd', 'en-US')).toBe('$49');
  });

  it('keeps two decimals for fractional amounts', () => {
    expect(formatMoney(1999, 'usd', 'en-US')).toBe('$19.99');
    expect(formatMoney(1950, 'usd', 'en-US')).toBe('$19.50');
  });

  it('accepts lowercase ISO codes for every supported currency', () => {
    expect(formatMoney(10000, 'eur', 'en-US')).toBe('€100');
    expect(formatMoney(10000, 'gbp', 'en-US')).toBe('£100');
    expect(formatMoney(10000, 'inr', 'en-US')).toBe('₹100');
    expect(formatMoney(10000, 'myr', 'en-US')).toContain('100');
    expect(formatMoney(10000, 'sgd', 'en-US')).toContain('100');
  });

  it('renders USD as "$" in non-US locales (no "US$" prefix)', () => {
    expect(formatMoney(4900, 'usd', 'en-GB')).toBe('$49');
    expect(formatMoney(4900, 'usd', 'en-MY')).toBe('$49');
    expect(formatMoney(4900, 'usd', 'en-SG')).toBe('$49');
    expect(formatPrice('monthly', 1999, 'usd', 'en-AU')).toBe('$19.99/mo');
  });

  it('keeps SGD distinguishable from USD', () => {
    expect(formatMoney(4900, 'sgd', 'en-US')).not.toBe('$49');
  });

  it('falls back gracefully for an invalid currency code', () => {
    expect(formatMoney(500, 'not-a-code', 'en-US')).toBe('5 NOT-A-CODE');
  });
});

describe('formatPrice', () => {
  it('labels free templates', () => {
    expect(formatPrice('free', 0, 'usd')).toBe('Free');
    expect(formatPrice(undefined, undefined, undefined)).toBe('Free');
    expect(formatPrice('one_time', 0, 'usd')).toBe('Free');
  });

  it('formats one-time and monthly prices', () => {
    expect(formatPrice('one_time', 4900, 'usd', 'en-US')).toBe('$49');
    expect(formatPrice('monthly', 1900, 'usd', 'en-US')).toBe('$19/mo');
  });
});

describe('isPaid', () => {
  it('requires a paid model and a positive price', () => {
    expect(isPaid('one_time', 100)).toBe(true);
    expect(isPaid('monthly', 0)).toBe(false);
    expect(isPaid('free', 500)).toBe(false);
    expect(isPaid(undefined, 500)).toBe(false);
  });
});

describe('majorToMinor / minorToMajorInput', () => {
  it('parses major units into cents without float drift', () => {
    expect(majorToMinor('49')).toBe(4900);
    expect(majorToMinor('19.99')).toBe(1999);
    expect(majorToMinor('0.1')).toBe(10);
    expect(majorToMinor('1,250.5')).toBe(125050);
    expect(majorToMinor(' 10. ')).toBe(1000);
    // 4.35 * 100 is 434.99999… in floating point — the string parser must not truncate.
    expect(majorToMinor('4.35')).toBe(435);
  });

  it('rejects invalid input', () => {
    expect(majorToMinor('')).toBeNull();
    expect(majorToMinor('abc')).toBeNull();
    expect(majorToMinor('-5')).toBeNull();
    expect(majorToMinor('1.999')).toBeNull();
    expect(majorToMinor('1e3')).toBeNull();
  });

  it('round-trips to an editable string', () => {
    expect(minorToMajorInput(4900)).toBe('49');
    expect(minorToMajorInput(1999)).toBe('19.99');
    expect(minorToMajorInput(1950)).toBe('19.50');
  });
});

describe('platform fee', () => {
  it('splits 20% to Flowamaz and 80% to the publisher', () => {
    expect(platformFee(4900, 20)).toBe(980);
    expect(publisherNet(4900, 20)).toBe(3920);
  });

  it('rounds the fee to the nearest minor unit and keeps fee + net = price', () => {
    for (const price of [100, 199, 333, 1999, 1_000_000]) {
      expect(platformFee(price, 20) + publisherNet(price, 20)).toBe(price);
    }
    expect(platformFee(199, 20)).toBe(40); // 39.8 → 40
    expect(publisherNet(199, 20)).toBe(159);
  });

  it('handles zero price and zero fee', () => {
    expect(platformFee(0, 20)).toBe(0);
    expect(publisherNet(0, 20)).toBe(0);
    expect(publisherNet(5000, 0)).toBe(5000);
  });
});

describe('validatePricingDraft', () => {
  it('always accepts free', () => {
    expect(validatePricingDraft({ pricingModel: 'free', amount: 'nonsense', currency: 'usd' })).toBeNull();
  });

  it('enforces the 100 – 1,000,000 minor unit range', () => {
    expect(validatePricingDraft({ pricingModel: 'one_time', amount: '0.99', currency: 'usd' })).toMatch(/between/);
    expect(validatePricingDraft({ pricingModel: 'one_time', amount: '1', currency: 'usd' })).toBeNull();
    expect(validatePricingDraft({ pricingModel: 'monthly', amount: '10000', currency: 'eur' })).toBeNull();
    expect(validatePricingDraft({ pricingModel: 'monthly', amount: '10000.01', currency: 'eur' })).toMatch(/between/);
  });

  it('explains unparseable amounts', () => {
    expect(validatePricingDraft({ pricingModel: 'one_time', amount: '', currency: 'usd' })).toMatch(/number/);
  });
});

describe('draft conversion', () => {
  it('converts a paid draft into the API payload', () => {
    expect(draftToPricing({ pricingModel: 'monthly', amount: '19.99', currency: 'gbp' })).toEqual({
      pricingModel: 'monthly',
      priceCents: 1999,
      currency: 'gbp',
    });
  });

  it('zeroes the price for free', () => {
    expect(draftToPricing({ pricingModel: 'free', amount: '50', currency: 'usd' })).toEqual({
      pricingModel: 'free',
      priceCents: 0,
      currency: 'usd',
    });
  });

  it('builds a draft from stored pricing, defaulting unknown currencies to usd', () => {
    expect(pricingToDraft({ pricingModel: 'one_time', priceCents: 4900, currency: 'MYR' })).toEqual({
      pricingModel: 'one_time',
      amount: '49',
      currency: 'myr',
    });
    expect(pricingToDraft({ pricingModel: 'free', priceCents: 0, currency: 'xyz' })).toEqual({
      pricingModel: 'free',
      amount: '',
      currency: 'usd',
    });
  });
});
