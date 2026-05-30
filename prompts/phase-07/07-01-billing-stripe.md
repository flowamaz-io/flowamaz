---
prompt-id: 07-01-billing-stripe
phase: 07
sequence: 1
roles: [Executor, Verifier, Security]
type: feature
depends-on: []
estimated-complexity: High
---

# Billing + Stripe — Subscription Management and Plan Upgrades

## Context
Phases 1-6 complete. Community edition limits enforced but no payment flow exists.
This prompt implements Stripe-based subscription management — plan selection, checkout,
webhook-driven status updates, and usage-based billing for AI tokens.

## Objective
Complete billing flow: free trial → paid plan upgrade via Stripe Checkout,
subscription lifecycle management, usage metering for AI tokens.

## Scope

### What to Build

**Stripe integration:**

NuGet: Stripe.net (latest stable)

`IStripeService + StripeService`:

`CreateCheckoutSessionAsync(orgId, planId, successUrl, cancelUrl, ct)`:
- Create Stripe Checkout Session for the selected plan
- Pre-fill customer email from org owner
- Set metadata: { orgId, planId }
- Return session URL for redirect

`CreateCustomerPortalSessionAsync(orgId, returnUrl, ct)`:
- Create Stripe Customer Portal session
- Allows self-service: upgrade, downgrade, cancel, update payment method
- Return portal URL

`HandleWebhookAsync(payload, stripeSignature, ct)`:
- Verify Stripe webhook signature (Stripe-Signature header)
- Handle events:
  - `checkout.session.completed` → activate subscription, update org plan
  - `customer.subscription.updated` → update plan on plan change
  - `customer.subscription.deleted` → downgrade to Community
  - `invoice.payment_failed` → send payment failed email, flag org
  - `invoice.payment_succeeded` → clear payment failed flag

**Billing entities:**

```
org_billing
  id, org_id (unique), stripe_customer_id, stripe_subscription_id,
  plan_id, plan_name, billing_period (monthly|annual),
  current_period_start, current_period_end,
  status (trialing|active|past_due|canceled|paused),
  trial_ends_at, canceled_at,
  created_at, updated_at
  
plan_definitions (seeded)
  id, name, stripe_price_id_monthly, stripe_price_id_annual,
  max_workflows, max_runs_month, max_members, max_workspaces,
  ai_token_monthly_limit, price_monthly_usd, price_annual_usd,
  features (jsonb array of feature strings)

usage_records (for metered billing)
  id, org_id, period_start, period_end,
  ai_tokens_used, runs_count, created_at
```

**Plans to seed:**

| Plan | Workflows | Runs/mo | Members | Price/mo |
|------|-----------|---------|---------|----------|
| Community | 5 | 500 | 1 | Free |
| Starter | 25 | 5,000 | 5 | $49 |
| Pro | Unlimited | 50,000 | 25 | $149 |
| Enterprise | Unlimited | Unlimited | Unlimited | Custom |

**API endpoints:**

`GET /api/v1/billing/plans` [AllowAnonymous] — list available plans
`GET /api/v1/billing/current` [OrgMember] — current org subscription
`POST /api/v1/billing/checkout` [OrgAdmin] — create Stripe checkout session
`POST /api/v1/billing/portal` [OrgAdmin] — create customer portal session
`POST /api/v1/billing/webhook` [AllowAnonymous] — Stripe webhook receiver
`GET /api/v1/billing/usage` [OrgAdmin] — current period usage

**Frontend — Billing & Plans:**

`PricingView.vue` (/pricing — public, no auth required):
- Monthly/Annual toggle (annual = 20% discount)
- Plan cards: Community | Starter | Pro | Enterprise
- Each card: price, feature list, [Get started] or [Upgrade] button
- Current plan highlighted with teal border
- Enterprise: [Contact sales] button → mailto:sales@flowamaz.io

`BillingSettingsView.vue` (/settings/billing — org admin only):
- Current plan + status badge (Active/Trial/Past due)
- Trial countdown: "12 days remaining in your trial"
- Usage bars: workflows used/limit, runs this month/limit, members/limit
- [Manage billing] → Stripe customer portal
- [Upgrade plan] → /pricing
- Invoice history (last 6 months, link to Stripe-hosted invoice)

**Trial expiry flow:**
When trial ends and no subscription:
- EditionService automatically returns Community limits
- Dashboard shows upgrade banner
- Upgrade prompts on limit hit (already exists from Phase 5)

**Environment variables:**
```
STRIPE_SECRET_KEY=sk_live_...
STRIPE_WEBHOOK_SECRET=whsec_...
STRIPE_STARTER_PRICE_ID_MONTHLY=price_...
STRIPE_STARTER_PRICE_ID_ANNUAL=price_...
STRIPE_PRO_PRICE_ID_MONTHLY=price_...
STRIPE_PRO_PRICE_ID_ANNUAL=price_...
```

**Unit tests:**
- StripeService: checkout.session.completed → org plan updated
- StripeService: subscription.deleted → org downgraded to Community
- StripeService: webhook invalid signature → throws
- StripeService: invoice.payment_failed → payment failed flag set
- BillingController: non-admin → 403 on billing endpoints

## Technical Requirements
- [ ] Stripe webhook signature verified (EventUtility.ConstructEvent)
- [ ] Idempotent webhook handling (store processed event IDs in Redis)
- [ ] Plan limits enforced via EditionService (already wired in Phase 5)
- [ ] Stripe customer ID stored in org_billing (never expose to frontend)
- [ ] All Stripe keys from environment variables only

## Acceptance Criteria
- [ ] Click Upgrade → Stripe Checkout opens
- [ ] Complete payment → plan updated to Starter/Pro
- [ ] Cancel subscription via portal → downgraded to Community
- [ ] Payment failed → email sent, org flagged
- [ ] /pricing renders without authentication

## Output Expected
```
backend/Flowamaz.Infrastructure/Services/StripeService.cs
backend/Flowamaz.Api/Controllers/BillingController.cs
backend/Flowamaz.Tests.Unit/Billing/StripeServiceTests.cs
web/src/views/billing/PricingView.vue
web/src/views/settings/BillingSettingsView.vue
```
