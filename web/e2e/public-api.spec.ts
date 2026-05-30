import { expect, request, test } from '@playwright/test';
import { API_URL, createTestWorkflow, publishWorkflow, setupReadyOrg, uniqueSuffix } from './fixtures/test-factories';

// Public API E2E (prompt 06-07, S51–S52). Runs against the full stack in the nightly e2e job.
test.describe('Public API (S51–S52)', () => {
  // S51: /api-docs renders the public API Scalar reference.
  test('S51: /api-docs renders the public API docs', async ({ page }) => {
    await page.goto('/api-docs');
    await expect(page.locator('body')).toContainText(/Flowamaz Public API|Scalar/i);
  });

  // S52: GET /api/public/v1/workflows with a workspace API key returns the published workflows.
  test('S52: public workflows endpoint returns published workflows for a valid API key', async ({ page }) => {
    const org = await setupReadyOrg(page);
    const wf = await createTestWorkflow(page, org, `pub-${uniqueSuffix()}`);
    await publishWorkflow(page, org, wf.id);

    // Mint an API key via the settings UI.
    await page.goto('/settings/api-keys');
    await page.getByRole('button', { name: /Create key/i }).click();
    await page.getByLabel('Name').fill('e2e-public');
    await page.getByRole('button', { name: 'Create key' }).click();
    const apiKey = (await page.locator('code').first().innerText()).trim();

    const api = await request.newContext();
    const res = await api.get(`${API_URL}/api/public/v1/workflows`, {
      headers: { Authorization: `Bearer ${apiKey}` },
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(JSON.stringify(body)).toContain('per_page'); // snake_case envelope
  });
});
