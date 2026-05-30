import { expect, test } from '@playwright/test';

// flowamaz.com marketing SPA. It is served separately from the app; point the tests at it via
// E2E_MARKETING_URL (default http://localhost:5174, the marketing Vite dev server / preview).
const MARKETING_URL = process.env.E2E_MARKETING_URL || 'http://localhost:5174';

test.describe('Marketing site (S75–S76)', () => {
  // S75: The marketing home page renders with no authentication.
  test('S75: flowamaz.com home renders without auth', async ({ page }) => {
    await page.goto(MARKETING_URL);

    await expect(
      page.getByRole('heading', { name: 'Automate anything. In plain English.' }),
    ).toBeVisible();
    // The primary CTA points at the app's register page.
    await expect(page.getByRole('link', { name: /Start your free trial/ })).toBeVisible();
  });

  // S76: The pricing page shows the three plan cards.
  test('S76: /pricing shows the plan cards', async ({ page }) => {
    await page.goto(`${MARKETING_URL}/pricing`);

    await expect(page.getByRole('heading', { name: /Pricing that scales/ })).toBeVisible();
    for (const plan of ['Community', 'Starter', 'Pro']) {
      await expect(page.getByRole('heading', { name: plan, exact: true })).toBeVisible();
    }
    // Monthly/annual toggle is present.
    await expect(page.getByRole('switch', { name: /annual billing/i })).toBeVisible();
  });
});
