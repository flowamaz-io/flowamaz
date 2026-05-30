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
    await expect(page.getByText('Pro', { exact: false })).toBeVisible();
    await expect(page.getByText('Enterprise', { exact: false })).toBeVisible();
  });

  // S62: clicking Upgrade kicks off the Stripe Checkout hand-off (or the not-configured message).
  test('S62: Upgrade starts the Stripe Checkout hand-off', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/settings/billing');

    const upgrade = page.getByRole('button', { name: /Upgrade/i }).first();
    await expect(upgrade).toBeVisible();

    // The click either navigates to a Stripe Checkout URL or shows the not-configured notice.
    const navigation = page.waitForURL(/checkout\.stripe\.com/, { timeout: 5000 }).catch(() => null);
    await upgrade.click();
    const navigated = await navigation;

    if (!navigated) {
      await expect(page.getByText(/billing is not configured|not configured/i)).toBeVisible();
    } else {
      expect(page.url()).toContain('checkout.stripe.com');
    }
  });
});
