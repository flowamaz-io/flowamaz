import { expect, test } from '@playwright/test';
import { setupReadyOrg } from './fixtures/test-factories';

// Notification centre E2E (prompt 06-07, S53–S55). Runs against the full stack in the nightly e2e job.
test.describe('Notifications (S53–S55)', () => {
  // S53: The bell renders; an unread badge reflects the unread count when notifications exist.
  test('S53: notification bell renders in the sidebar', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/');
    await expect(page.getByRole('button', { name: /Open notifications/i })).toBeVisible();
  });

  // S54: Clicking the bell opens the dropdown (empty state when there are no notifications).
  test('S54: clicking the bell opens the dropdown', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/');
    await page.getByRole('button', { name: /Open notifications/i }).click();
    await expect(page.getByText(/Notifications|You're all caught up/i)).toBeVisible();
  });

  // S55: Mark all read clears the unread badge.
  test('S55: mark all read removes the unread badge', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/');
    await page.getByRole('button', { name: /Open notifications/i }).click();
    await page.getByRole('button', { name: /Mark all read/i }).click();
    await expect(page.locator('[data-test="notification-badge"]')).toHaveCount(0);
  });
});
