import { expect, request, test } from '@playwright/test';
import {
  API_URL, canvasNodeCenter, createWorkflowFor, publishWorkflowFor, setupReadyOrg, triggerInstanceFor, uniqueSuffix,
} from './fixtures/test-factories';

// Library Forms and Packs (FUNCTIONAL.md §23.2), S82–S85. Runs against the full stack nightly.

const FORM_GATE_YAML = [
  'workflow: { id: e2e-form-gate, version: v1, name: E2E form gate }',
  'nodes:',
  '  - { id: start, type: Trigger }',
  '  - id: approve',
  '    type: HumanGate',
  '    label: Manager approval',
  '    config:',
  '      form:',
  '        fields:',
  '          - { key: amount, label: Approved amount, type: number, required: true, min: 0 }',
  '          - { key: cost_centre, label: Cost centre, type: text, required: true }',
  '  - { id: done, type: End }',
  'edges:',
  '  - { id: e1, from: start, to: approve }',
  '  - { id: e2, from: approve, to: done }',
].join('\n');

const PLAIN_GATE_YAML = [
  'workflow: { id: e2e-plain-gate, version: v1, name: E2E plain gate }',
  'nodes:',
  '  - { id: start, type: Trigger }',
  '  - { id: approve, type: HumanGate, label: Approve }',
  '  - { id: done, type: End }',
  'edges:',
  '  - { id: e1, from: start, to: approve }',
  '  - { id: e2, from: approve, to: done }',
].join('\n');

/** Waits until the instance's gate is pending in the workspace queue. */
async function waitForGate(token: string, workspaceId: string, instanceId: string): Promise<void> {
  const api = await request.newContext();
  try {
    await expect.poll(async () => {
      const res = await api.get(`${API_URL}/api/v1/workspaces/${workspaceId}/gates`, { headers: { Authorization: `Bearer ${token}` } });
      const body = (await res.json()) as { data?: { instanceId: string }[] };
      return (body.data ?? []).some((g) => g.instanceId === instanceId);
    }, { timeout: 30_000 }).toBe(true);
  } finally {
    await api.dispose();
  }
}

test.describe('Library forms and packs (S82–S85)', () => {
  // S82: a gate with a form asks the approver for its fields, refuses blanks and shows the answers afterwards.
  test('S82: approving a gate with a form collects the answers', async ({ page }) => {
    const ready = await setupReadyOrg(page);
    const wf = await createWorkflowFor(ready, `form-gate-${uniqueSuffix()}`, FORM_GATE_YAML);
    await publishWorkflowFor(ready, wf.id);
    const instanceId = await triggerInstanceFor(ready, wf.id);
    await waitForGate(ready.org.accessToken, ready.workspace.id, instanceId);

    await page.goto('/gates');
    await expect(page.getByTestId('gate-has-form').first()).toContainText('Asks for 2 details');
    await page.getByTestId('gate-approve-approve').first().click();

    const dialog = page.getByRole('dialog', { name: /^Approve: / });
    await expect(dialog).toBeVisible();
    await dialog.getByTestId('gate-approve-submit').click();
    await expect(dialog.getByText('Approved amount is required.')).toBeVisible();

    await dialog.getByLabel(/Approved amount/).fill('1200');
    await dialog.getByLabel(/Cost centre/).fill('CC-7');
    await dialog.getByTestId('gate-approve-submit').click();
    await expect(dialog).toBeHidden();

    const answers = page.getByTestId('gate-form-answers').first();
    await expect(answers).toContainText('Approved amount');
    await expect(answers).toContainText('1200');
    await expect(answers).toContainText('CC-7');
  });

  // S83: a Library form is copied onto a gate from the node config panel.
  test('S83: "Use a Library form" copies the fields into the gate YAML', async ({ page }) => {
    const ready = await setupReadyOrg(page);
    const wf = await createWorkflowFor(ready, `form-cfg-${uniqueSuffix()}`, PLAIN_GATE_YAML);

    await page.goto(`/workflows/${wf.id}/edit?workspaceId=${ready.workspace.id}`);
    const at = await canvasNodeCenter(page, 'approve');
    await page.mouse.dblclick(at.x, at.y);
    const panel = page.getByRole('dialog', { name: /Node configuration/i });
    await expect(panel.locator('[data-test="gate-form-empty"]')).toBeVisible();
    await panel.locator('[data-test="gate-form-library"]').click();
    await panel.locator('[data-test="gate-form-use-purchase-details"]').click();
    await expect(panel.locator('[data-test="gate-form-field-0"]')).toBeVisible();
    await panel.getByRole('button', { name: 'Save node' }).click();
    await expect(panel).toBeHidden();
    await expect(page.locator('.cm-content')).toContainText('approved_amount');
  });

  // S84: Library → Forms lists the official forms.
  test('S84: Library Forms tab lists the official forms', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/library?tab=forms');
    const form = page.getByTestId('library-form-purchase-details');
    await expect(form).toBeVisible();
    await expect(form).toContainText('Official');
    await form.getByRole('button', { name: 'Show fields' }).click();
    await expect(form).toContainText('Cost centre');
  });

  // S85: installing a pack adds its connectors and workflows, once.
  test('S85: installing a pack adds what is missing', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/library?tab=packs');
    const pack = page.getByTestId('library-pack-finance-approvals');
    await expect(pack).toBeVisible();
    await pack.getByTestId('library-pack-install-finance-approvals').click();
    await expect(pack.getByTestId('library-pack-result-finance-approvals')).toContainText('5 added');
    await expect(pack.getByTestId('library-pack-install-finance-approvals')).toHaveText(/Everything is installed/);
    await expect(pack).toContainText('in this workspace');
  });
});
