import { expect, test } from '@playwright/test';
import { createTestWorkflow, setupReadyOrg, uniqueSuffix } from './fixtures/test-factories';

// ROI analytics E2E (prompt 05-08, S42–S43). Runs against the full stack in the nightly e2e job.
test.describe('ROI Analytics (S42–S43)', () => {
  // S42: /analytics → ROI view renders with date range picker.
  test('S42: analytics view renders with date range picker', async ({ page }) => {
    await setupReadyOrg(page);

    await page.goto('/analytics');
    await expect(page.getByRole('heading', { name: 'ROI Analytics' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'This month' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Last 3 months' })).toBeVisible();
  });

  // S43: Configure ROI for a workflow → numbers appear in the table.
  test('S43: configuring ROI surfaces the workflow in the table', async ({ page }) => {
    const org = await setupReadyOrg(page);
    const wf = await createTestWorkflow(page, org, `roi-${uniqueSuffix()}`);

    await page.goto('/analytics');
    // The workflow row exposes a Configure action.
    const row = page.getByRole('row', { name: new RegExp(wf.name, 'i') });
    await row.getByRole('button', { name: /Configure|Edit/ }).click();

    await page.getByLabel(/How long did this process take manually/i).fill('30');
    await page.locator('input[type="number"]').nth(1).fill('20');
    await page.locator('input[type="number"]').nth(2).fill('2');
    await page.getByRole('button', { name: 'Save baseline' }).click();

    await expect(page.getByText('Configure ROI')).toBeHidden();
    await expect(row).toBeVisible();
  });
});
