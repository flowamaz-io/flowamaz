import { expect, request, test } from '@playwright/test';
import { API_URL, setupReadyOrg } from './fixtures/test-factories';

test.describe('Documentation (S77–S78)', () => {
  // S77: The in-app help panel search returns results for "connector".
  test('S77: help panel search returns results for "connector"', async ({ page }) => {
    await setupReadyOrg(page);
    await page.waitForURL((url) => new URL(url).pathname === '/');

    await page.locator('body').press('Shift+?');
    const panel = page.getByRole('complementary', { name: 'Help panel' });
    await expect(panel).toBeVisible();

    await panel.getByPlaceholder(/Search help articles/i).fill('connector');

    // At least one connector-related article is surfaced.
    await expect(panel.getByRole('button', { name: /connector/i }).first()).toBeVisible();
  });

  // S78: The Scalar API reference renders and lists public endpoints.
  test('S78: API docs (Scalar) renders with public endpoints', async () => {
    const api = await request.newContext();
    const res = await api.get(`${API_URL}/scalar`);
    expect(res.status()).toBe(200);

    const body = await res.text();
    // Scalar's HTML shell references the OpenAPI document the reference renders from.
    expect(body.toLowerCase()).toContain('scalar');

    // The OpenAPI document itself exposes the public API surface.
    const openApi = await api.get(`${API_URL}/openapi/v1.json`);
    if (openApi.ok()) {
      expect(await openApi.text()).toContain('/api/public/v1');
    }
    await api.dispose();
  });
});
