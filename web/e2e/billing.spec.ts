import { expect, test } from '@playwright/test';
import { setupReadyOrg } from './fixtures/test-factories';

/**
 * Billing & pricing (S61–S62). The pricing page is public (no auth); the in-app upgrade flow hands
 * off to Stripe Checkout. S62 requires Stripe to be configured (STRIPE_SECRET_KEY) so the backend
 * can mint a Checkout URL; against a deployment without billing configured it surfaces the
 * "billing not configured" message instead — both are exercised here without leaving the app.
 */
test.describe('Billing (S61–S62)', () => {
  // S61: /pricing renders the public plan grid without any authentication.
  test('S61: /pricing renders the plan grid without auth', async ({ page }) => {
    await page.goto('/pricing');
    await expect(page.getByRole('heading', { name: /Plans|Pricing/ })).toBeVisible();
    // The four standard plans are listed.
    await expect(page.getByRole('heading', { name: 'Pro', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Enterprise', exact: true })).toBeVisible();
  });

  // S62: clicking Upgrade kicks off the Stripe Checkout hand-off (or the not-configured message).
  test('S62: Upgrade starts the Stripe Checkout hand-off', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/settings/billing');

    // Billing settings' Upgrade opens the plan grid; the Pro card's Upgrade starts checkout.
    await page.getByRole('button', { name: /Upgrade/i }).first().click();
    await page.waitForURL('**/pricing');
    const upgrade = page.getByRole('button', { name: 'Upgrade', exact: true });
    await expect(upgrade).toBeVisible();

    // The click either navigates to a Stripe Checkout URL or shows the not-configured notice.
    const navigation = page.waitForURL(/checkout\.stripe\.com/, { timeout: 5000 }).catch(() => null);
    await upgrade.click();
    const navigated = await navigation;

    if (!navigated) {
      await expect(page.getByText(/Billing is not configured on this deployment/)).toBeVisible();
    } else {
      expect(page.url()).toContain('checkout.stripe.com');
    }
  });
});
