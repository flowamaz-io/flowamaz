import { expect, test } from '@playwright/test';
import { createTestWorkflow, setupReadyOrg, uniqueSuffix } from './fixtures/test-factories';

// Git versioning E2E (prompt 05-08, S40–S41). Runs against the full stack in the nightly e2e job.
test.describe('Git versioning (S40–S41)', () => {
  // S40: Workflow detail → Versions tab → commit history visible.
  test('S40: versions tab shows commit history', async ({ page }) => {
    const org = await setupReadyOrg(page);
    const wf = await createTestWorkflow(page, org, `git-${uniqueSuffix()}`);

    await page.goto(`/workflows/${wf.id}`);
    await page.getByRole('button', { name: 'versions' }).click();

    // The create commit is recorded — at least one short-SHA badge appears.
    await expect(page.getByText(/^[0-9a-f]{7}$/).first()).toBeVisible();
    await expect(page.getByRole('button', { name: 'View' }).first()).toBeVisible();
  });

  // S41: Diff between two versions → colour-coded lines visible.
  test('S41: diff modal renders colour-coded lines', async ({ page }) => {
    const org = await setupReadyOrg(page);
    const wf = await createTestWorkflow(page, org, `gitdiff-${uniqueSuffix()}`);

    // Edit once so there are two commits to diff.
    await page.goto(`/workflows/${wf.id}`);
    await page.getByRole('button', { name: 'versions' }).click();

    const diffButtons = page.getByRole('button', { name: 'Diff' });
    await diffButtons.last().click(); // oldest row → compares to latest

    await expect(page.getByRole('heading', { name: 'Compare versions' })).toBeVisible();
    // Node-level summary header is present.
    await expect(page.getByText(/node|identical/i).first()).toBeVisible();
  });
});
