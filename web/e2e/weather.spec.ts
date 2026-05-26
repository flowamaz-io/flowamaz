import { expect, test } from '@playwright/test';
import { setupReadyOrg } from './fixtures/test-factories';

test.describe('Workflow Weather (S31–S32)', () => {
  // S31: /weather shows the weather page with header.
  test('S31: weather page loads with header and workflow grid', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/weather');
    await expect(page.getByRole('heading', { name: 'Workflow Weather' })).toBeVisible();
  });

  // S32: weather page has a Refresh button.
  test('S32: weather page has a Refresh button', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/weather');
    await expect(page.getByRole('button', { name: 'Refresh' })).toBeVisible();
  });
});
