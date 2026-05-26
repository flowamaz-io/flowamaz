import { expect, request, test } from '@playwright/test';
import { createTestWorkflow, publishWorkflow, setupReadyOrg } from './fixtures/test-factories';

// Requires the full stack (backend + web + db + redis). The list/trigger UI is exercised here;
// instance progression depends on the background worker being enabled in the run environment.
test.describe('Workflows (S18–S20)', () => {
  // S18: empty /workflows shows the teaching empty state with creation-method preview.
  test('S18: empty workflows list shows the create CTA', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/workflows');
    await expect(page.getByRole('heading', { name: 'Create your first workflow' })).toBeVisible();
  });

  // S19: a published workflow created via the API appears in the list.
  test('S19: a published workflow appears in the list', async ({ page }) => {
    const { org, workspace } = await setupReadyOrg(page);
    const api = await request.newContext();
    const id = await createTestWorkflow(api, org.accessToken, workspace.id, 'Order Flow');
    await publishWorkflow(api, org.accessToken, workspace.id, id);
    await api.dispose();

    await page.goto('/workflows');
    await expect(page.getByText('Order Flow')).toBeVisible();
  });

  // S20: triggering from the list creates a run that shows up in /instances.
  test('S20: triggering from the list creates a run in /instances', async ({ page }) => {
    const { org, workspace } = await setupReadyOrg(page);
    const api = await request.newContext();
    const id = await createTestWorkflow(api, org.accessToken, workspace.id, 'Trigger Flow');
    await publishWorkflow(api, org.accessToken, workspace.id, id);
    await api.dispose();

    await page.goto('/workflows');
    // Top "Trigger run" opens the modal; the modal's "Trigger run" submits.
    await page.getByRole('button', { name: 'Trigger run' }).first().click();
    // Wait for the trigger POST to finish before navigating — otherwise the goto below aborts the
    // in-flight request and no instance is created.
    const triggered = page.waitForResponse(
      (r) => new URL(r.url()).pathname.endsWith('/instances') && r.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'Trigger run' }).last().click();
    await triggered;

    await page.goto('/instances');
    // Scope to the table body — the status filter <select> has matching (hidden) <option>s.
    await expect(page.locator('tbody').getByText(/Pending|Running|Completed/).first()).toBeVisible();
  });
});
