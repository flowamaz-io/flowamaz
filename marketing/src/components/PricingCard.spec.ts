import { describe, it, expect } from 'vitest';
import { mount } from '@vue/test-utils';
import PricingCard from './PricingCard.vue';
import { PLANS, registerUrlForPlan } from '@/constants';

const starter = PLANS.find((p) => p.slug === 'starter')!;

describe('PricingCard', () => {
  it('shows the monthly price when annual is off', () => {
    const wrapper = mount(PricingCard, { props: { plan: starter, annual: false } });
    expect(wrapper.get('[data-test="price"]').text()).toBe(`$${starter.monthlyUsd}`);
  });

  it('shows the discounted annual price when annual is on', () => {
    const wrapper = mount(PricingCard, { props: { plan: starter, annual: true } });
    expect(wrapper.get('[data-test="price"]').text()).toBe(`$${starter.annualUsd}`);
  });

  it('links the CTA to register with the plan slug', () => {
    const wrapper = mount(PricingCard, { props: { plan: starter, annual: false } });
    expect(wrapper.get('a').attributes('href')).toBe(registerUrlForPlan('starter'));
  });
});
