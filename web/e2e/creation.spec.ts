import { expect, test } from '@playwright/test';
import { setupReadyOrg } from './fixtures/test-factories';

test.describe('Workflow Creation (S33)', () => {
  // S33: /workflows/new has a workflow name input.
  test('S33: new workflow page has workflow name input for clone detection', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/workflows/new');
    await expect(page.getByPlaceholder(/invoice approval/i)).toBeVisible();
  });
});
