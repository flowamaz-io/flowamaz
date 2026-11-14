import { expect, test } from '@playwright/test';
import { createWorkflowFor, setupReadyOrg } from './fixtures/test-factories';

// Workflow Twin (S92–S93): simulate a workflow from its Twin tab and see the badge on the workflow list.
// The simulation runs nothing for real; the workflow below has the spec's own bug (< and > leave 5000 unrouted).

const PURCHASE_YAML = `apiVersion: flowamaz/v1
kind: Workflow
metadata: { id: purchase-twin, name: Purchase approval }
spec:
  variables:
    amount: { type: number, required: true }
  nodes:
    - { id: start, type: trigger }
    - id: size
      type: router
      label: Amount check
      config:
        branches:
          - { label: Small, condition: "{{ amount }} < 5000" }
          - { label: Large, condition: "{{ amount }} > 5000" }
    - { id: done, type: end }
  edges:
    - { from: start, to: size }
    - { from: size, to: done, via: Small }
    - { from: size, to: done, via: Large }
`;

test.describe('Workflow Twin (S92–S93)', () => {
  test('S92: running the Twin finds "breaks when amount = 5000" with a fix', async ({ page }) => {
    const ready = await setupReadyOrg(page);
    const wf = await createWorkflowFor(ready, 'Purchase approval', PURCHASE_YAML);

    await page.goto(`/workflows/${wf.id}?tab=twin`);
    await expect(page.getByTestId('twin-empty')).toBeVisible();
    await page.getByTestId('twin-run').click();

    const finding = page.getByTestId('twin-finding').filter({ hasText: 'Breaks when amount = 5000' });
    await expect(finding).toBeVisible();
    await expect(finding).toContainText('Nothing handles exactly 5000');
    await expect(page.getByTestId('twin-status')).toContainText('break');
    await expect(page.getByTestId('twin-meta')).toContainText('saved draft');
  });

  test('S93: the workflow list shows the Twin badge and it opens the Twin tab', async ({ page }) => {
    const ready = await setupReadyOrg(page);
    const wf = await createWorkflowFor(ready, 'Purchase approval', PURCHASE_YAML);
    await page.goto(`/workflows/${wf.id}?tab=twin`);
    await page.getByTestId('twin-run').click();
    await expect(page.getByTestId('twin-finding').first()).toBeVisible();

    await page.goto('/workflows');
    const badge = page.getByTestId(`twin-badge-${wf.id}`);
    await expect(badge).toContainText('break');
    await badge.click();
    await expect(page).toHaveURL(new RegExp(`/workflows/${wf.id}\\?tab=twin`));
    await expect(page.getByTestId('twin-panel')).toBeVisible();
  });
});
