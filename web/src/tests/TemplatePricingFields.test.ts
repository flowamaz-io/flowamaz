import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import TemplatePricingFields from '@/components/library/TemplatePricingFields.vue';
import TemplatePriceBadge from '@/components/library/TemplatePriceBadge.vue';
import type { PricingDraft } from '@/types/marketplace';

describe('TemplatePricingFields', () => {
  it('hides price inputs for free', () => {
    const wrapper = mount(TemplatePricingFields, {
      props: { modelValue: { pricingModel: 'free', amount: '', currency: 'usd' } as PricingDraft },
    });
    expect(wrapper.find('input').exists()).toBe(false);
  });

  it('shows the live publisher earnings line after the platform fee', () => {
    const wrapper = mount(TemplatePricingFields, {
      props: { modelValue: { pricingModel: 'one_time', amount: '49', currency: 'usd' } as PricingDraft, feePercent: 20 },
    });
    const line = wrapper.find('[data-test="earnings-line"]').text();
    expect(line).toContain('You receive $39.20 per sale after the 20% Flowamaz fee');
  });

  it('emits an updated draft when the pricing model changes', async () => {
    const wrapper = mount(TemplatePricingFields, {
      props: { modelValue: { pricingModel: 'free', amount: '', currency: 'usd' } as PricingDraft },
    });
    const monthly = wrapper.findAll('button[role="radio"]').find((b) => b.text() === 'Monthly');
    await monthly!.trigger('click');
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([
      { pricingModel: 'monthly', amount: '', currency: 'usd' },
    ]);
  });
});

describe('TemplatePriceBadge', () => {
  it('renders Free, one-time and monthly labels', () => {
    expect(mount(TemplatePriceBadge).text()).toBe('Free');
    expect(
      mount(TemplatePriceBadge, { props: { pricingModel: 'one_time', priceCents: 4900, currency: 'usd' } }).text(),
    ).toBe('$49');
    expect(
      mount(TemplatePriceBadge, { props: { pricingModel: 'monthly', priceCents: 1900, currency: 'usd' } }).text(),
    ).toBe('$19/mo');
  });
});
