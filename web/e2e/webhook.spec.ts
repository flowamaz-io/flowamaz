import { expect, test } from '@playwright/test';
import { createWorkflowFor, publishWorkflowFor, setupReadyOrg, uniqueSuffix } from './fixtures/test-factories';

// Webhook triggers E2E (prompt 06-07, S49–S50). Runs against the full stack in the nightly e2e job.
test.describe('Webhook triggers (S49–S50)', () => {
  // S49: Create a webhook endpoint → URL is shown with a curl example.
  test('S49: create webhook endpoint shows URL + curl example', async ({ page }) => {
    const org = await setupReadyOrg(page);
    const wf = await createWorkflowFor(org, `hook-${uniqueSuffix()}`);
    await publishWorkflowFor(org, wf.id);

    await page.goto('/settings/webhooks');
    await page.getByRole('button', { name: /Create webhook/i }).first().click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Workflow').selectOption({ label: wf.name });
    await dialog.getByRole('button', { name: 'Create webhook' }).click();

    // The one-time secret + endpoint URL are shown.
    const reveal = page.getByRole('alert');
    await expect(reveal.getByText(/Copy your signing secret now/)).toBeVisible();
    // The endpoint URL is built from the deployment's base URL (Platform__BaseUrl).
    await expect(reveal.locator('code', { hasText: /\/webhooks\/[0-9a-f-]{36}$/ })).toBeVisible();
  });

  // S50: The workflow detail page surfaces the webhook URL + curl example once an endpoint exists.
  test('S50: workflow detail shows the webhook trigger section', async ({ page }) => {
    const org = await setupReadyOrg(page);
    const wf = await createWorkflowFor(org, `hook-detail-${uniqueSuffix()}`);
    await publishWorkflowFor(org, wf.id);

    await page.goto('/settings/webhooks');
    await page.getByRole('button', { name: /Create webhook/i }).first().click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Workflow').selectOption({ label: wf.name });
    await dialog.getByRole('button', { name: 'Create webhook' }).click();

    await page.goto(`/workflows/${wf.id}`);
    await expect(page.getByRole('heading', { name: /Trigger via webhook/i })).toBeVisible();
    await expect(page.getByText(/curl -X POST/)).toBeVisible();
  });
});
