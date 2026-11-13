import { expect, test } from '@playwright/test';
import { createWorkflowFor, publishWorkflowFor, setupReadyOrg, uniqueSuffix } from './fixtures/test-factories';

/**
 * Workflow templates (S65–S67). The library lists the seeded official templates; installing a
 * template opens the new workflow on the canvas; a published workflow can be published as a
 * community template from its detail page.
 */
test.describe('Templates (S65–S67)', () => {
  // S65: the Templates tab in the library shows the official templates.
  test('S65: Templates tab shows official templates', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/library');
    await page.getByRole('tab', { name: /Templates/i }).click();
    await expect(page.getByRole('tab', { name: /Templates/i })).toHaveAttribute('aria-selected', 'true');
    // At least one official template card is listed.
    await expect(page.getByText('Official', { exact: true }).first()).toBeVisible();
  });

  // S66: installing a template opens the canvas with the template's nodes.
  test('S66: install template → canvas opens', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/library?tab=templates');

    // The card's Install opens a dialog to name the new workflow; its Install creates it.
    await page.getByRole('button', { name: 'Install', exact: true }).first().click();
    await page.getByRole('dialog').getByRole('button', { name: 'Install', exact: true }).click();
    await page.waitForURL(/\/workflows\/[^/]+\/edit/);
    await expect(page.locator('canvas').first()).toBeVisible();
  });

  // S67: a published workflow can be published as a community template from its detail page.
  test('S67: publish template from a workflow detail page', async ({ page }) => {
    const ready = await setupReadyOrg(page);
    const wf = await createWorkflowFor(ready, `tmpl-${uniqueSuffix()}`);
    await publishWorkflowFor(ready, wf.id);

    await page.goto(`/workflows/${wf.id}`);
    await page.getByRole('button', { name: /Publish as template/i }).click();
    await expect(page.getByRole('dialog')).toBeVisible();
  });
});
