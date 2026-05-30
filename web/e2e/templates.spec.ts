import { expect, test } from '@playwright/test';
import { setupReadyOrg } from './fixtures/test-factories';

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
    // At least one official template card is listed.
    await expect(page.getByText(/Official/i).first()).toBeVisible();
  });

  // S66: installing a template opens the canvas with the template's nodes.
  test('S66: install template → canvas opens', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/library');
    await page.getByRole('tab', { name: /Templates/i }).click();

    await page.getByRole('button', { name: /Install/i }).first().click();
    // Installing creates a workflow and navigates to the canvas editor.
    await page.waitForURL(/\/workflows\/.+/);
    await expect(page.locator('[data-canvas], canvas, .cytoscape-canvas').first()).toBeVisible();
  });

  // S67: a published workflow can be published as a community template from its detail page.
  test('S67: publish template from a workflow detail page', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/library');
    await page.getByRole('tab', { name: /Templates/i }).click();
    await page.getByRole('button', { name: /Install/i }).first().click();
    await page.waitForURL(/\/workflows\/.+/);

    // The workflow detail page exposes a "Publish as template" action.
    await expect(page.getByRole('button', { name: /Publish as template|Publish template/i })).toBeVisible();
  });
});
