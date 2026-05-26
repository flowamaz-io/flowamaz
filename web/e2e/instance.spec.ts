import { type APIRequestContext, expect, request, test } from '@playwright/test';
import {
  API_URL,
  createTestWorkflow,
  publishWorkflow,
  setupReadyOrg,
  triggerInstance,
} from './fixtures/test-factories';

/** Polls the instance API until it reaches a terminal status (worker drives trigger→end to done). */
async function waitForCompletion(api: APIRequestContext, token: string, ws: string, instanceId: string): Promise<void> {
  for (let i = 0; i < 30; i++) {
    const res = await api.get(`${API_URL}/api/v1/workspaces/${ws}/instances/${instanceId}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const status = (((await res.json()) as { data?: { status?: string } }).data?.status) ?? '';
    if (['Completed', 'Failed', 'Cancelled'].includes(status)) return;
    await new Promise((r) => setTimeout(r, 500));
  }
}

// Requires the full stack with the background worker enabled (so trigger→end reaches Completed)
// and a configured platform key (the CEO narrative resolves the F5 model).
test.describe('Instance detail (S21–S23)', () => {
  async function setup(page: import('@playwright/test').Page) {
    const { org, workspace } = await setupReadyOrg(page);
    const api = await request.newContext();
    const id = await createTestWorkflow(api, org.accessToken, workspace.id);
    await publishWorkflow(api, org.accessToken, workspace.id, id);
    const instanceId = await triggerInstance(api, org.accessToken, workspace.id, id);
    await waitForCompletion(api, org.accessToken, workspace.id, instanceId);
    await api.dispose();
    return instanceId;
  }

  // S21: timeline tab renders node rows with duration bars.
  test('S21: instance timeline shows node rows', async ({ page }) => {
    const instanceId = await setup(page);
    await page.goto(`/instances/${instanceId}`);
    await page.getByRole('button', { name: 'timeline' }).click();
    await expect(page.getByText(/ms$/).first()).toBeVisible();
  });

  // S22: narrative tab loads the CEO narrative.
  test('S22: CEO narrative loads', async ({ page }) => {
    const instanceId = await setup(page);
    await page.goto(`/instances/${instanceId}`);
    await page.getByRole('button', { name: 'narrative' }).click();
    await page.getByRole('button', { name: 'CEO' }).click();
    await expect(page.getByText(/Completed/)).toBeVisible();
  });

  // S23: switching to the Auditor narrative shows different (event-log) content.
  test('S23: Auditor narrative differs from CEO', async ({ page }) => {
    const instanceId = await setup(page);
    await page.goto(`/instances/${instanceId}`);
    await page.getByRole('button', { name: 'narrative' }).click();
    await page.getByRole('button', { name: 'Auditor' }).click();
    // The Auditor narrative is the deterministic compliance record (unique heading + event log).
    await expect(page.getByRole('heading', { name: /Compliance Record/ })).toBeVisible();
  });
});
