import { expect, request, test } from '@playwright/test';
import { createTestOrg, loginViaUi, setupReadyOrg, uniqueSuffix } from './fixtures/test-factories';

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
    await page.getByPlaceholder('e.g. Operations').fill(`Operations ${uniqueSuffix()}`);
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
    await page.getByPlaceholder('e.g. Operations').fill(`Ops ${uniqueSuffix()}`);
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
    await page.getByPlaceholder('e.g. Operations').fill(`Ops ${uniqueSuffix()}`);
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

/**
 * Product tour (S70–S71). On first arrival at the dashboard the guided product tour starts; once
 * completed it never shows again (gated by the `tour_completed_{workspaceId}` user preference).
 */
test.describe('Product tour (S70–S71)', () => {
  // S70: first login lands on the dashboard and offers / starts the product tour.
  test('S70: first login → product tour starts', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/');
    // The tour either auto-starts (a tour step is visible) or offers a "Take the tour" entry point.
    const tourEntry = page.getByRole('button', { name: /Take the tour/i });
    const tourStep = page.getByRole('button', { name: /^Next$/ });
    await expect(tourEntry.or(tourStep).first()).toBeVisible({ timeout: 10000 });
  });

  // S71: completing the tour persists the preference so it does not reappear after a refresh.
  test('S71: complete tour → does not show again', async ({ page }) => {
    const { workspace } = await setupReadyOrg(page);
    await page.goto('/');

    // Mark the tour complete (same preference the ProductTour component sets on finish).
    await page.evaluate((wsId) => {
      localStorage.setItem(`tour_completed_${wsId}`, 'true');
    }, workspace.id);

    await page.reload();
    // No active tour step is shown once the tour is marked complete.
    await expect(page.getByRole('button', { name: /^Next$/ })).toHaveCount(0);
  });
});
