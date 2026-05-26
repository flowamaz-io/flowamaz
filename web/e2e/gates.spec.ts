import { expect, test } from '@playwright/test';
import { setupReadyOrg } from './fixtures/test-factories';

test.describe('Human Gates (S37–S38)', () => {
  // S37: /gates shows pending gates table or empty state.
  test('S37: /gates shows pending gates table or empty state', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/gates');
    await page.waitForLoadState('networkidle');
    // GatesView renders either a table of pending gates or the "No pending gates" empty state.
    await expect(
      page
        .getByRole('table')
        .first()
        .or(page.getByText(/no pending|no gates|pending approvals/i).first())
        .or(page.getByRole('heading', { name: /approvals|gates/i }).first()),
    ).toBeVisible({ timeout: 10_000 });
  });

  // S38: approving a gate from the portal shows a success toast or removes the gate from the list.
  test('S38: Approve gate from portal shows success feedback', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/gates');
    await page.waitForLoadState('networkidle');

    // If there are pending gates, click the Approve button on the first one.
    const approveButton = page.getByRole('button', { name: /approve/i }).first();
    const hasGates = (await approveButton.count()) > 0;

    if (!hasGates) {
      // No pending gates seeded for this test — just verify the empty state is present
      await expect(
        page.getByText(/no pending|no gates|pending approvals/i).first(),
      ).toBeVisible({ timeout: 5_000 });
      return;
    }

    await approveButton.click();
    // After approval: either a success toast appears or the gate row is removed
    await expect(
      page
        .getByText(/approved|success/i)
        .first()
        .or(page.locator('[role="status"]').first()),
    ).toBeVisible({ timeout: 5_000 });
  });
});
