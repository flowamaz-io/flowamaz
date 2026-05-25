import { expect, request, test } from '@playwright/test';
import {
  createTestOrg,
  loginViaUi,
  resetRateLimits,
  seedOnboardingComplete,
  setupReadyOrg,
  uniqueSuffix,
} from './fixtures/test-factories';

test.describe('Authentication (S1–S6)', () => {
  // S1: Register new organisation → onboarding wizard appears on first login.
  test('S1: register a new org redirects to the onboarding wizard', async ({ page }) => {
    await resetRateLimits();
    const suffix = uniqueSuffix();
    const email = `owner-${suffix}@e2e.flowamaz.test`;

    await page.goto('/register');
    // First plan card is Community; pick Starter so registration creates a hosted trial.
    await page.getByRole('button', { name: /Starter/ }).click();
    // Target inputs by placeholder — FmInput's help tooltip pollutes the accessible label. The
    // org-name suffix is unique, so the auto-generated org-URL slug is unique too (no need to set it).
    await page.getByPlaceholder('Acme Inc.').fill(`E2E Org ${suffix}`);
    await page.getByPlaceholder('Jane Doe').fill('E2E Owner');
    await page.getByPlaceholder('jane@acme.com').fill(email);
    await page.getByPlaceholder('At least 8 characters').fill('Sup3rSecret!23');
    await page.getByRole('button', { name: 'Create organisation' }).click();

    await page.waitForURL('**/onboarding');
    // Wizard step 1 is "Name your first workspace".
    await expect(page.getByRole('heading', { name: 'Name your first workspace' })).toBeVisible();
  });

  // S2: Login with valid credentials → dashboard loads with getting-started checklist.
  test('S2: valid login lands on the dashboard with the getting-started checklist', async ({ page }) => {
    await setupReadyOrg(page);

    await page.waitForURL((url) => new URL(url).pathname === '/');
    await expect(page.getByRole('heading', { name: /Welcome back/ })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Get started with Flowamaz' })).toBeVisible();
  });

  // S3: Login with wrong password → actionable error shown, stays on /login.
  test('S3: wrong password shows an actionable error and stays on /login', async ({ page }) => {
    const api = await request.newContext();
    const org = await createTestOrg(api);
    await api.dispose();

    await page.goto('/login');
    await page.getByPlaceholder('you@company.com').fill(org.email);
    await page.getByPlaceholder('acme').fill(org.orgSlug);

    for (let attempt = 0; attempt < 3; attempt++) {
      await page.locator('input[type="password"]').fill(`wrong-${attempt}`);
      await page.getByRole('button', { name: 'Sign in' }).click();
      // The backend's actionable message for bad credentials (no user enumeration).
      await expect(page.getByText(/invalid credentials/i)).toBeVisible();
      await expect(page).toHaveURL(/\/login$/);
    }
  });

  // S4: Account lockout after 5 failed attempts (uses a FRESH user).
  test('S4: account locks after 5 failed attempts; 6th shows the lockout message', async ({ page }) => {
    const api = await request.newContext();
    const org = await createTestOrg(api);
    await api.dispose();

    await page.goto('/login');
    await page.getByPlaceholder('you@company.com').fill(org.email);
    await page.getByPlaceholder('acme').fill(org.orgSlug);

    // 5 wrong attempts: each rejected as invalid credentials; the 5th trips the lockout server-side.
    for (let attempt = 0; attempt < 5; attempt++) {
      await page.locator('input[type="password"]').fill(`wrong-${attempt}`);
      await page.getByRole('button', { name: 'Sign in' }).click();
      await expect(page.getByText(/invalid credentials|temporarily locked/i)).toBeVisible();
    }

    // 6th attempt: account is locked — message differs from "invalid credentials".
    await page.locator('input[type="password"]').fill('wrong-final');
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page.getByText(/temporarily locked/i)).toBeVisible();
    await expect(page).toHaveURL(/\/login$/);
  });

  // S5: Shift+? opens the help panel with the article mapped for the current screen.
  test('S5: Shift+? opens the help panel to the dashboard article', async ({ page }) => {
    await setupReadyOrg(page);
    await page.waitForURL((url) => new URL(url).pathname === '/');

    // "?" is Shift+/ — the global handler ignores keys typed in form fields, so press on body.
    await page.locator('body').press('Shift+?');

    const panel = page.getByRole('complementary', { name: 'Help panel' });
    await expect(panel).toBeVisible();
    // articleMap['/'] = getting-started/what-is-flowamaz → article body H1 "What is Flowamaz?".
    await expect(
      panel.getByRole('article').getByRole('heading', { name: 'What is Flowamaz?', level: 1 }),
    ).toBeVisible();
  });

  // S6: Logout clears the session → /settings redirects to /login.
  test('S6: logout clears the session and /settings redirects to /login', async ({ page }) => {
    const { org } = await setupReadyOrg(page);
    await page.waitForURL((url) => new URL(url).pathname === '/');

    // The "Sign out" item lives in the avatar dropdown — open it via the avatar trigger
    // (last button in the header), then click Sign out.
    await page.locator('header').getByRole('button').last().click();
    await page.getByRole('button', { name: 'Sign out' }).click();
    await page.waitForURL('**/login');

    // Re-seed onboarding so a guard redirect can only be auth-driven, then hit /settings directly.
    await seedOnboardingComplete(page, org.orgId);
    await page.goto('/settings');
    await expect(page).toHaveURL(/\/login(\?|$)/);
  });
});
