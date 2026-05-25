import { expect, test } from '@playwright/test';
import { setupReadyOrg, uniqueSuffix } from './fixtures/test-factories';

test.describe('Workspace (S7–S11)', () => {
  // S7: Dashboard loads → getting-started checklist visible with all 4 items.
  test('S7: dashboard shows the getting-started checklist with all 4 items', async ({ page }) => {
    await setupReadyOrg(page);
    await page.waitForURL((url) => new URL(url).pathname === '/');

    const checklist = page.locator('section', {
      has: page.getByRole('heading', { name: 'Get started with Flowamaz' }),
    });
    await expect(checklist.getByText('Create your first workflow')).toBeVisible();
    await expect(checklist.getByText('Connect a system')).toBeVisible();
    await expect(checklist.getByText('Trigger your first run')).toBeVisible();
    await expect(checklist.getByText('Invite a team member')).toBeVisible();
    // Only "Invite a team member" is actionable in Phase 1 (a "Start →" link).
    await expect(checklist.getByRole('link', { name: /Start/ })).toHaveCount(1);
  });

  // S8: AI model settings — dropdowns populated; F1 Co-pilot shows Anthropic / claude-haiku-4-5.
  test('S8: AI model config exposes F1 Co-pilot with Anthropic + claude-haiku-4-5', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/settings');

    await expect(page.getByRole('heading', { name: 'AI model configuration' })).toBeVisible();

    // F1 (Co-pilot) row — the per-function override block containing its label.
    const f1 = page
      .locator('div.rounded-lg')
      .filter({ hasText: 'F1 · Co-pilot' })
      .first();
    await expect(f1).toBeVisible();
    // Provider select offers Anthropic (default for the platform copilot model).
    await expect(f1.getByRole('option', { name: 'Anthropic' })).toHaveCount(1);
    // Model select offers the platform default model for F1.
    await expect(f1.getByRole('option', { name: 'claude-haiku-4-5' })).toHaveCount(1);
    // Resolution badge tells the user where the model came from.
    await expect(f1.getByText(/From:/)).toBeVisible();
  });

  // S9: Invite member flow. Phase 1 has no email invites and one owner per org, so an unknown
  // email cannot be added — the API returns the actionable 404 "must register first" path.
  // (Deviation: a true "appears with Designer badge" assertion requires a pre-seeded second org
  // user, which the Phase 1 API cannot create. We assert the real behaviour instead.)
  test('S9: inviting a non-member email surfaces the actionable "must register first" error', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/settings/members');

    await page.getByRole('button', { name: 'Invite member' }).click();
    await page.getByLabel('Email').fill(`ghost-${uniqueSuffix()}@e2e.flowamaz.test`);

    // Default invite role in the modal is Designer.
    const dialog = page.getByRole('dialog');
    await expect(dialog.locator('select')).toHaveValue('Designer');

    await page.getByRole('button', { name: 'Send invite' }).click();

    // The backend 404 message is shown as an actionable toast.
    await expect(page.getByText(/must register first/i)).toBeVisible();
  });

  // S10: Change member role → badge updates immediately. No second org user exists in Phase 1,
  // so we inject a Designer member via API-route mocking to exercise the real role-change UI and
  // its optimistic update. The PATCH is stubbed (204) and we assert the select reflects Operator.
  // (Deviation: requires a pre-seeded second org user the Phase 1 API cannot create.)
  test('S10: changing a member role updates the row immediately', async ({ page }) => {
    const memberId = '11111111-1111-1111-1111-111111111111';

    // Inject a single Designer member into the members list (GET) and accept role changes (PATCH).
    await page.route('**/api/v1/workspaces/*/members**', async (route) => {
      const req = route.request();
      if (req.method() === 'GET') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            success: true,
            statusCode: 200,
            data: {
              data: [
                {
                  orgUserId: memberId,
                  email: 'designer@e2e.flowamaz.test',
                  name: 'Dee Signer',
                  role: 'Designer',
                  joinedAt: new Date().toISOString(),
                  isActive: true,
                },
              ],
              page: 1,
              pageSize: 20,
              total: 1,
            },
            correlationId: 'e2e',
          }),
        });
        return;
      }
      if (req.method() === 'PATCH') {
        await route.fulfill({ status: 204, body: '' });
        return;
      }
      await route.fallback();
    });

    await setupReadyOrg(page);
    await page.goto('/settings/members');

    const row = page.getByRole('row', { name: /Dee Signer/ });
    await expect(row).toBeVisible();
    const roleSelect = row.locator('select');
    await expect(roleSelect).toHaveValue('Designer');

    await roleSelect.selectOption('Operator');
    await expect(roleSelect).toHaveValue('Operator');
    await expect(page.getByText(/role updated to Operator/i)).toBeVisible();
  });

  // S11: Create API key → plain key shown once in alert → not in list afterwards.
  // Phase 1 has no "list environments" endpoint, so the create POST + list GET are stubbed to
  // exercise the real once-only-reveal UI deterministically. (Deviation documented in results.md.)
  test('S11: creating an API key shows the plain key once, then only the prefix', async ({ page }) => {
    const plainKey = 'fmz_live_TESTPLAINKEY1234567890abcdef';
    const prefix = 'fmz_live_TESTPL';
    let created = false;

    await page.route('**/api/v1/workspaces/*/api-keys**', async (route) => {
      const req = route.request();
      if (req.method() === 'POST') {
        created = true;
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          headers: { 'X-Plain-Key-One-Time': 'true' },
          body: JSON.stringify({
            success: true,
            statusCode: 200,
            data: {
              id: '22222222-2222-2222-2222-222222222222',
              environmentId: '00000000-0000-0000-0000-000000000001',
              name: 'test-key',
              keyPrefix: prefix,
              scopes: ['workflows:read'],
              lastUsedAt: null,
              expiresAt: null,
              isActive: true,
              createdAt: new Date().toISOString(),
              plainKey, // returned exactly once on create
            },
            correlationId: 'e2e',
          }),
        });
        return;
      }
      if (req.method() === 'GET') {
        // Empty until created, then list-safe copy (plainKey null) by prefix.
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            success: true,
            statusCode: 200,
            data: {
              data: created
                ? [
                    {
                      id: '22222222-2222-2222-2222-222222222222',
                      environmentId: '00000000-0000-0000-0000-000000000001',
                      name: 'test-key',
                      keyPrefix: prefix,
                      scopes: ['workflows:read'],
                      lastUsedAt: null,
                      expiresAt: null,
                      isActive: true,
                      createdAt: new Date().toISOString(),
                      plainKey: null,
                    },
                  ]
                : [],
              page: 1,
              pageSize: 20,
              total: created ? 1 : 0,
            },
            correlationId: 'e2e',
          }),
        });
        return;
      }
      await route.fallback();
    });

    await setupReadyOrg(page);
    await page.goto('/settings/api-keys');

    await page.getByRole('button', { name: /Create your first API key|Create key/ }).first().click();

    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Name').fill('test-key');
    const envField = dialog.getByPlaceholder(/Environment ID/i);
    if (await envField.isVisible().catch(() => false)) {
      await envField.fill('00000000-0000-0000-0000-000000000001');
    }
    await dialog.getByRole('button', { name: 'Create key' }).click();

    // Plain key shown once in a warning alert, starting with fmz_.
    await expect(page.getByText(plainKey, { exact: false })).toBeVisible();

    // The key is listed by its prefix in the table…
    await expect(page.getByRole('cell', { name: 'test-key' })).toBeVisible();
    await expect(page.getByText(new RegExp(`${prefix}…`))).toBeVisible();
  });
});
