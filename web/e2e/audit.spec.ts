import { expect, request, test } from '@playwright/test';
import { createTestWorkflow, setupReadyOrg } from './fixtures/test-factories';

/**
 * Audit log (S63–S64). Creating a workflow records an immutable audit event that surfaces in
 * Settings → Audit; the org owner can export the feed as a CSV download.
 */
test.describe('Audit (S63–S64)', () => {
  // S63: creating a workflow produces an audit event visible in the audit log.
  test('S63: create workflow → audit event appears in /settings/audit', async ({ page }) => {
    const { org, workspace } = await setupReadyOrg(page);
    const api = await request.newContext();
    await createTestWorkflow(api, org.accessToken, workspace.id, 'Audited Flow');
    await api.dispose();

    await page.goto('/settings/audit');
    await expect(page.getByRole('heading', { name: /Audit/ })).toBeVisible();
    // The workflow.created event is recorded fire-and-forget; it shows up shortly.
    await expect(page.getByText(/created/i).first()).toBeVisible({ timeout: 10000 });
  });

  // S64: exporting the audit log triggers a CSV file download.
  test('S64: export CSV downloads a file', async ({ page }) => {
    const { org, workspace } = await setupReadyOrg(page);
    const api = await request.newContext();
    await createTestWorkflow(api, org.accessToken, workspace.id, 'Export Flow');
    await api.dispose();

    await page.goto('/settings/audit');
    await expect(page.getByRole('heading', { name: /Audit/ })).toBeVisible();

    const downloadPromise = page.waitForEvent('download');
    await page.getByRole('button', { name: /Export CSV/i }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toMatch(/\.csv$/);
  });
});
