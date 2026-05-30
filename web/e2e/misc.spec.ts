import { expect, test } from '@playwright/test';
import { createTestWorkflow, setupReadyOrg, uniqueSuffix } from './fixtures/test-factories';

// Miscellaneous Phase 6 UX fixes E2E (prompt 06-07, S60). Runs against the full stack nightly.
test.describe('App shell UX fixes (S60)', () => {
  // S60: Opening a workflow on the canvas shows its real name, not "Untitled Workflow".
  test('S60: canvas title shows the workflow name', async ({ page }) => {
    const org = await setupReadyOrg(page);
    const wf = await createTestWorkflow(page, org, `title-${uniqueSuffix()}`);

    await page.goto(`/workflows/${wf.id}/edit?workspaceId=${org.workspaceId}`);
    await expect(page.getByText(wf.name)).toBeVisible();
    await expect(page.getByText('Untitled Workflow')).toHaveCount(0);
  });
});
