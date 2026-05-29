import { expect, test } from '@playwright/test';
import { setupReadyOrg } from './fixtures/test-factories';

// Sidebar E2E (prompt 05-08, S44–S46). Runs against the full stack in the nightly e2e job.
test.describe('Dark sidebar (S44–S46)', () => {
  // S44: Workspace switcher opens dropdown, shows workspaces.
  test('S44: workspace switcher opens and lists workspaces', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/');

    // The switcher lives in the sidebar header; clicking it reveals the workspace list + search.
    await page.getByTestId('workspace-switcher').click().catch(async () => {
      // Fallback if no test-id: click the current workspace name region.
      await page.locator('aside').getByText(/workspace/i).first().click();
    });
    await expect(page.getByPlaceholder(/Search/i)).toBeVisible();
  });

  // S45: Sidebar collapse → icon-only mode.
  test('S45: collapse toggle switches to icon-only width', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/');

    const aside = page.locator('aside');
    await page.getByRole('button', { name: /Collapse sidebar/i }).click();
    // Collapsed desktop width is 52px.
    await expect(aside).toHaveClass(/md:w-\[52px\]/);
  });

  // S46: Community edition → edition banner visible. Requires the stack running EDITION=community.
  test('S46: community edition shows the edition banner', async ({ page }) => {
    test.skip(process.env.E2E_EDITION !== 'community', 'Edition banner only renders in Community edition');
    await setupReadyOrg(page);
    await page.goto('/');
    await expect(page.getByText('Community Edition')).toBeVisible();
    await expect(page.getByRole('link', { name: /Upgrade/ })).toBeVisible();
  });
});
