import { type APIRequestContext, type Page, expect, request, test } from '@playwright/test';
import {
  API_URL,
  createTestWorkflow,
  publishWorkflow,
  setupReadyOrg,
  triggerInstance,
} from './fixtures/test-factories';

/** Polls the instance API until it reaches a terminal status. */
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

/**
 * Advanced monitoring (S68–S69). The instance timeline reveals node input/output on expand; a
 * terminal instance can be replayed, producing a new test instance.
 */
test.describe('Monitoring (S68–S69)', () => {
  async function setupCompletedInstance(page: Page): Promise<{ workspaceId: string; instanceId: string }> {
    const { org, workspace } = await setupReadyOrg(page);
    const api = await request.newContext();
    const workflowId = await createTestWorkflow(api, org.accessToken, workspace.id);
    await publishWorkflow(api, org.accessToken, workspace.id, workflowId);
    const instanceId = await triggerInstance(api, org.accessToken, workspace.id, workflowId);
    await waitForCompletion(api, org.accessToken, workspace.id, instanceId);
    await api.dispose();
    return { workspaceId: workspace.id, instanceId };
  }

  // S68: expanding a node on the timeline reveals its input/output snapshot.
  test('S68: instance timeline shows node I/O on expand', async ({ page }) => {
    const { instanceId } = await setupCompletedInstance(page);
    await page.goto(`/instances/${instanceId}`);
    await page.getByRole('button', { name: 'timeline' }).click();

    // Expand the first node row to reveal its I/O snapshot.
    await page.getByText(/ms$/).first().click();
    await expect(page.getByText(/Input|Output/i).first()).toBeVisible();
  });

  // S69: replaying a completed instance creates a new test instance.
  test('S69: replay → new test instance created', async ({ page }) => {
    const { instanceId } = await setupCompletedInstance(page);
    await page.goto(`/instances/${instanceId}`);

    await page.getByRole('button', { name: /Replay/i }).click();
    // The replay lands on a new instance flagged as a test run.
    await page.waitForURL((url) => /\/instances\/.+/.test(new URL(url).pathname) && !url.includes(instanceId));
    await expect(page.getByText(/Test/i).first()).toBeVisible();
  });
});
