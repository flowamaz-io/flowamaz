import { expect, test } from '@playwright/test';
import { createWorkflowFor, publishWorkflowFor, setupReadyOrg, triggerInstanceFor, uniqueSuffix } from './fixtures/test-factories';

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
    const wf = await createWorkflowFor(org, `roi-${uniqueSuffix()}`);
    // The table lists workflows that ran in the period, so give it one run.
    await publishWorkflowFor(org, wf.id);
    await triggerInstanceFor(org, wf.id);

    await page.goto('/analytics');
    // The workflow row exposes a Configure action.
    const row = page.getByRole('row', { name: new RegExp(wf.name, 'i') });
    await row.getByRole('button', { name: /Configure|Edit/ }).click();

    await page.getByLabel(/How long did this process take manually/i).fill('30');
    await page.getByLabel(/Fully-loaded cost per manual run/i).fill('20');
    await page.getByLabel(/Automated cost per run/i).fill('2');
    await page.getByRole('button', { name: 'Save baseline' }).click();

    await expect(page.getByRole('dialog')).toBeHidden();
    await expect(row.getByRole('button', { name: 'Edit' })).toBeVisible();
    await expect(row).toBeVisible();
  });
});
