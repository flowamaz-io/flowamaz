// Cross-domain links: marketing (flowamaz.com) points users at the app and docs.
export const APP_URL = 'https://app.flowamaz.io';
export const REGISTER_URL = `${APP_URL}/register`;
export const DOCS_URL = 'https://docs.flowamaz.io';
export const GITHUB_URL = 'https://github.com/flowamaz-io';

export function registerUrlForPlan(plan: string): string {
  return `${REGISTER_URL}?plan=${encodeURIComponent(plan)}`;
}

export interface PlanDef {
  slug: string;
  name: string;
  monthlyUsd: number;
  annualUsd: number;
  tagline: string;
  features: string[];
  featured?: boolean;
}

// Mirrors the app /pricing plan cards. Annual price is the effective monthly rate billed yearly.
export const PLANS: PlanDef[] = [
  {
    slug: 'community',
    name: 'Community',
    monthlyUsd: 0,
    annualUsd: 0,
    tagline: 'Self-hosted, free forever.',
    features: ['5 workflows', '500 runs / month', '1 member', 'Community support'],
  },
  {
    slug: 'starter',
    name: 'Starter',
    monthlyUsd: 49,
    annualUsd: 39,
    tagline: 'For small teams getting started.',
    features: ['Unlimited workflows', '10,000 runs / month', '10 members', 'All 13 connectors', 'Email support'],
    featured: true,
  },
  {
    slug: 'pro',
    name: 'Pro',
    monthlyUsd: 149,
    annualUsd: 119,
    tagline: 'For teams running mission-critical automation.',
    features: ['Everything in Starter', '100,000 runs / month', 'Unlimited members', 'SSO (SAML/OIDC)', 'Audit log + ROI analytics', 'Priority support'],
  },
];
