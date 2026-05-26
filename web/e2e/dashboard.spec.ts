import { expect, request, test } from '@playwright/test';
import { createTestWorkflow, publishWorkflow, setupReadyOrg } from './fixtures/test-factories';

// Requires the full stack. The dashboard cards + Workflow Weather widget read live APIs.
test.describe('Dashboard (S24–S25)', () => {
  // S24: metric cards show real numbers (not the Phase-1 "--" placeholders).
  test('S24: dashboard metric cards show real numbers', async ({ page }) => {
    const { org, workspace } = await setupReadyOrg(page);
    const api = await request.newContext();
    const id = await createTestWorkflow(api, org.accessToken, workspace.id, 'Dash Flow');
    await publishWorkflow(api, org.accessToken, workspace.id, id);
    await api.dispose();

    await page.goto('/');
    // "Total workflows" card resolves to a digit once the API responds.
    const card = page.locator('div', { hasText: 'Total workflows' }).last();
    await expect(card.getByText(/^\d+$/)).toBeVisible();
  });

  // S25: the Workflow Weather widget renders.
  test('S25: Workflow Weather widget is visible', async ({ page }) => {
    const { org, workspace } = await setupReadyOrg(page);
    const api = await request.newContext();
    const id = await createTestWorkflow(api, org.accessToken, workspace.id, 'Weather Flow');
    await publishWorkflow(api, org.accessToken, workspace.id, id);
    await api.dispose();

    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Workflow weather' })).toBeVisible();
  });
});
