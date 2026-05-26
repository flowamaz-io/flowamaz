import { expect, request, test } from '@playwright/test';
import { createTestWorkflow, setupReadyOrg } from './fixtures/test-factories';

test.describe('Workflow Empathy (S39)', () => {
  // S39: Empathy toggle in the workflow editor shows the EmpathyPanel.
  test('S39: Empathy toggle shows EmpathyPanel', async ({ page }) => {
    const { org, workspace } = await setupReadyOrg(page);
    const api = await request.newContext();
    const id = await createTestWorkflow(api, org.accessToken, workspace.id, 'Empathy Test');
    await api.dispose();

    await page.goto(`/workflows/${id}/edit`);
    // Wait for the editor toolbar to confirm the editor loaded
    await page.getByText('Edit workflow').waitFor({ timeout: 10_000 });

    // WorkflowEditorView renders two mode buttons: the second one is "Empathy"
    const empathyButton = page.getByRole('button', { name: 'Empathy' });
    await expect(empathyButton).toBeVisible();
    await empathyButton.click();

    // EmpathyPanel should become visible after clicking the toggle
    // The panel contains "Workflow Empathy" heading or empathy-related content
    await expect(
      page
        .getByText(/workflow empathy|empathy score|health/i)
        .first()
        .or(page.locator('[data-testid="empathy-panel"]')),
    ).toBeVisible({ timeout: 5_000 });
  });
});
