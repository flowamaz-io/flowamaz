import { expect, test } from '@playwright/test';
import { canvasNodeCenter, createWorkflowFor, setupReadyOrg, uniqueSuffix } from './fixtures/test-factories';

const GATE_WORKFLOW_YAML = [
  'workflow: { id: e2e-gate, version: v1, name: E2E gate }',
  'nodes:',
  '  - { id: start, type: Trigger }',
  '  - { id: approve, type: HumanGate, label: Approve }',
  '  - { id: done, type: End }',
  'edges:',
  '  - { id: e1, from: start, to: approve }',
  '  - { id: e2, from: approve, to: done }',
].join('\n');

// Node config editor E2E (prompt 06-07, S58–S59). Runs against the full stack nightly.
test.describe('Node config editor (S58–S59)', () => {
  // S58: Double-clicking a node opens the configuration panel.
  test('S58: double-click a node opens the config panel', async ({ page }) => {
    const org = await setupReadyOrg(page);
    const wf = await createWorkflowFor(org, `cfg-${uniqueSuffix()}`);

    await page.goto(`/workflows/${wf.id}/edit?workspaceId=${org.workspace.id}`);
    const at = await canvasNodeCenter(page, 'start');
    await page.mouse.dblclick(at.x, at.y);
    await expect(page.getByRole('dialog', { name: /Node configuration/i })).toBeVisible();
  });

  // S59: Configuring a human-gate node persists into the workflow YAML config.
  test('S59: human-gate assignee is saved to the YAML config', async ({ page }) => {
    const org = await setupReadyOrg(page);
    const wf = await createWorkflowFor(org, `gate-cfg-${uniqueSuffix()}`, GATE_WORKFLOW_YAML);

    await page.goto(`/workflows/${wf.id}/edit?workspaceId=${org.workspace.id}`);
    const at = await canvasNodeCenter(page, 'approve');
    await page.mouse.dblclick(at.x, at.y);
    const panel = page.getByRole('dialog', { name: /Node configuration/i });
    await expect(panel).toBeVisible();

    await panel.locator('[data-test="gate-assignee"]').selectOption('Operator');
    await panel.getByRole('button', { name: 'Save node' }).click();
    await expect(panel).toBeHidden();
    // The YAML pane reflects the new assignee.
    await expect(page.locator('.cm-content')).toContainText('Operator');
  });
});
