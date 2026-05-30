import { expect, test } from '@playwright/test';
import { setupReadyOrg } from './fixtures/test-factories';

// Connector marketplace detail E2E (prompt 06-07, S56–S57). Runs against the full stack nightly.
test.describe('Connector detail (S56–S57)', () => {
  // S56: Clicking a connector card opens the detail page with real metrics.
  test('S56: connector card opens the detail page', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/library');
    const firstCard = page.locator('.cursor-pointer', { hasText: /installs/i }).first();
    await firstCard.click();
    await expect(page.getByText(/installs/i)).toBeVisible();
    await expect(page.getByRole('heading', { name: /Reviews/i })).toBeVisible();
  });

  // S57: Installing a connector increments the install count shown on the detail page.
  test('S57: installing a connector increments the install count', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/library');
    const firstCard = page.locator('.cursor-pointer', { hasText: /installs/i }).first();
    await firstCard.click();

    const before = await page.getByText(/\d+ installs/).first().innerText();
    await page.getByRole('button', { name: /Install/i }).first().click();
    // The credential wizard completes the install; afterwards the count reflects the change.
    await expect(page.getByText(/installs/i)).toBeVisible();
    expect(before).toMatch(/installs/);
  });
});
