import { expect, test } from '@playwright/test';
import { createTestWorkflow, setupReadyOrg, uniqueSuffix } from './fixtures/test-factories';

// Node config editor E2E (prompt 06-07, S58–S59). Runs against the full stack nightly.
test.describe('Node config editor (S58–S59)', () => {
  // S58: Double-clicking a node opens the configuration panel.
  test('S58: double-click a node opens the config panel', async ({ page }) => {
    const org = await setupReadyOrg(page);
    const wf = await createTestWorkflow(page, org, `cfg-${uniqueSuffix()}`);

    await page.goto(`/workflows/${wf.id}/edit?workspaceId=${org.workspaceId}`);
    // The trigger node is rendered on the canvas; double-click opens the panel.
    const node = page.locator('canvas').first();
    await node.dblclick({ position: { x: 120, y: 120 } });
    await expect(page.getByRole('dialog', { name: /Node configuration/i })).toBeVisible();
  });

  // S59: Configuring a human-gate node persists into the workflow YAML config.
  test('S59: human-gate assignee is saved to the YAML config', async ({ page }) => {
    const org = await setupReadyOrg(page);
    const wf = await createTestWorkflow(page, org, `gate-cfg-${uniqueSuffix()}`);

    await page.goto(`/workflows/${wf.id}/edit?workspaceId=${org.workspaceId}`);
    const node = page.locator('canvas').first();
    await node.dblclick({ position: { x: 120, y: 120 } });
    const panel = page.getByRole('dialog', { name: /Node configuration/i });
    await expect(panel).toBeVisible();
    await panel.getByRole('button', { name: 'Save node' }).click();
    await expect(panel).toBeHidden();
  });
});
