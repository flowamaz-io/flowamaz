import { expect, test } from '@playwright/test';
import { seedOnboardingComplete, setupReadyOrg } from './fixtures/test-factories';

/** Route → expected help-panel article title (mirrors web/src/help/articleMap.ts). */
const ARTICLE_TITLE_BY_ROUTE: Record<string, string> = {
  '/': 'What is Flowamaz?',
  '/settings': 'What is a Workspace?',
  '/settings/members': 'Invite Team Members',
  '/settings/api-keys': 'API Keys',
};

test.describe('Help (S15–S17)', () => {
  // S15: Help panel search "invite" → invite-team-members article first, click renders it.
  test('S15: searching "invite" surfaces and opens the Invite Team Members article', async ({ page }) => {
    await setupReadyOrg(page);
    await page.waitForURL((url) => new URL(url).pathname === '/');

    await page.locator('body').press('Shift+?');
    const panel = page.getByRole('complementary', { name: 'Help panel' });
    await expect(panel).toBeVisible();

    await panel.getByPlaceholder(/Search help articles/i).fill('invite');

    // First result is the invite-team-members article.
    const firstResult = panel.getByRole('button', { name: /Invite Team Members/ }).first();
    await expect(firstResult).toBeVisible();
    await firstResult.click();

    // Article renders — the Invite Team Members body H1 appears in the renderer.
    await expect(
      panel.getByRole('article').getByRole('heading', { name: 'Invite Team Members', level: 1 }),
    ).toBeVisible();
  });

  // S16: All Phase 1 routes map to the correct help article (loop, not 7 separate tests).
  test('S16: each Phase 1 route opens its mapped help article', async ({ page }) => {
    const { org } = await setupReadyOrg(page);
    await seedOnboardingComplete(page, org.orgId);

    for (const [route, title] of Object.entries(ARTICLE_TITLE_BY_ROUTE)) {
      await page.goto(route);
      await page.waitForURL((url) => new URL(url).pathname === route);

      await page.locator('body').press('Shift+?');
      const panel = page.getByRole('complementary', { name: 'Help panel' });
      await expect(panel).toBeVisible();

      // The mapped article is rendered — its body H1 (matching the title) appears in the article.
      await expect(panel.getByRole('article').getByRole('heading', { name: title, level: 1 })).toBeVisible();

      // Close before the next route so the next Shift+? re-opens cleanly.
      await panel.getByRole('button', { name: 'Close help' }).click();
      await expect(panel).toBeHidden();
    }
  });

  // S17: Error state → help article link works.
  // We trigger the members-list error state via a stubbed 403, assert FmErrorState renders with a
  // help link, click it, and verify the help panel opens to the wired article.
  // NOTE: Phase 1 views pass an explicit help-article to FmErrorState (MembersView →
  // workspaces/invite-team-members). The generic 403→roles-and-permissions mapping lives in
  // errorArticleMap but is only used when a view omits an explicit article. We assert the real
  // wired behaviour here. (Deviation documented in results.md.)
  test('S17: an error state exposes a working help-article link', async ({ page }) => {
    await page.route('**/api/v1/workspaces/*/members**', async (route) => {
      if (route.request().method() !== 'GET') return route.fallback();
      await route.fulfill({
        status: 403,
        contentType: 'application/json',
        body: JSON.stringify({
          success: false,
          statusCode: 403,
          code: 'FORBIDDEN',
          message: 'You need the Admin role in this workspace to manage members. Ask an org owner for access.',
          correlationId: 'e2e',
        }),
      });
    });

    await setupReadyOrg(page);
    await page.goto('/settings/members');

    // FmErrorState is shown (never a bare "Something went wrong").
    await expect(page.getByRole('heading', { name: "Couldn't load members" })).toBeVisible();

    const helpLink = page.getByRole('button', { name: /Read the help article/ });
    await expect(helpLink).toBeVisible();
    await helpLink.click();

    const panel = page.getByRole('complementary', { name: 'Help panel' });
    await expect(panel).toBeVisible();
    await expect(
      panel.getByRole('article').getByRole('heading', { name: 'Invite Team Members', level: 1 }),
    ).toBeVisible();
  });
});
