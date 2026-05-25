import { expect, request, test } from '@playwright/test';
import { createTestOrg, loginViaUi, uniqueSuffix } from './fixtures/test-factories';

test.describe('Onboarding (S12–S14)', () => {
  // S12: Onboarding wizard completes all 3 steps → dashboard.
  test('S12: completing the 3-step wizard lands on the dashboard with the checklist', async ({ page }) => {
    const api = await request.newContext();
    const org = await createTestOrg(api);
    await api.dispose();

    await loginViaUi(page, org);
    await page.waitForURL('**/onboarding');

    // Step 1 — name the workspace and continue (creates it via the API).
    await expect(page.getByRole('heading', { name: 'Name your first workspace' })).toBeVisible();
    await page.getByLabel('Workspace name').fill(`Operations ${uniqueSuffix()}`);
    await page.getByRole('button', { name: 'Continue' }).click();

    // Step 2 — creation methods preview.
    await expect(page.getByRole('heading', { name: 'Your workflows will build themselves' })).toBeVisible();
    await page.getByRole('button', { name: 'Continue' }).click();

    // Step 3 — invite; skip it.
    await expect(page.getByRole('heading', { name: 'Invite your first team member' })).toBeVisible();
    await page.getByRole('button', { name: 'Skip for now' }).click();

    // Dashboard with the getting-started checklist.
    await page.waitForURL((url) => new URL(url).pathname === '/');
    await expect(page.getByRole('heading', { name: 'Get started with Flowamaz' })).toBeVisible();
  });

  // S13: Onboarding resumes from the last step on page refresh.
  test('S13: onboarding resumes at step 2 after a refresh', async ({ page }) => {
    const api = await request.newContext();
    const org = await createTestOrg(api);
    await api.dispose();

    await loginViaUi(page, org);
    await page.waitForURL('**/onboarding');

    // Complete step 1 → wizard advances to step 2 and persists progress.
    await page.getByLabel('Workspace name').fill(`Ops ${uniqueSuffix()}`);
    await page.getByRole('button', { name: 'Continue' }).click();
    await expect(page.getByRole('heading', { name: 'Your workflows will build themselves' })).toBeVisible();

    // Refresh — should resume at step 2, NOT bounce back to step 1.
    await page.reload();
    await expect(page.getByRole('heading', { name: 'Your workflows will build themselves' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Name your first workspace' })).toHaveCount(0);
  });

  // S14: Getting-started checklist: dismiss → does not reappear on refresh.
  test('S14: dismissing the checklist keeps it hidden after a refresh', async ({ page }) => {
    const api = await request.newContext();
    const org = await createTestOrg(api);
    await api.dispose();

    // Walk the full wizard so onboarding is complete and we land on the dashboard.
    await loginViaUi(page, org);
    await page.waitForURL('**/onboarding');
    await page.getByLabel('Workspace name').fill(`Ops ${uniqueSuffix()}`);
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByRole('button', { name: 'Skip for now' }).click();
    await page.waitForURL((url) => new URL(url).pathname === '/');

    await expect(page.getByRole('heading', { name: 'Get started with Flowamaz' })).toBeVisible();
    await page.getByRole('button', { name: /explore on my own/i }).click();
    await expect(page.getByRole('heading', { name: 'Get started with Flowamaz' })).toHaveCount(0);

    await page.reload();
    await expect(page.getByRole('heading', { name: 'Get started with Flowamaz' })).toHaveCount(0);
  });
});
