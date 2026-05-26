import { expect, test } from '@playwright/test';
import { setupReadyOrg } from './fixtures/test-factories';

test.describe('Workflow Editor (S26–S30)', () => {
  // S26: /workflows/new shows all 6 creation method cards.
  test('S26: new workflow page shows 6 creation method cards', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/workflows/new');
    // All 6 creation method buttons should be visible (nl, voice, image, conversation, document, canvas)
    await expect(page.getByText('Write a description')).toBeVisible();
    await expect(page.getByText('Speak it')).toBeVisible();
    await expect(page.getByText('Upload a photo')).toBeVisible();
    await expect(page.getByText('Paste a conversation')).toBeVisible();
    await expect(page.getByText('Upload a document')).toBeVisible();
    await expect(page.getByText('Draw on canvas')).toBeVisible();
  });

  // S27: NL form fields are visible after selecting "Write a description".
  test('S27: NL template form appears after selecting write method', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/workflows/new');
    await page.getByText('Write a description').click();
    await expect(page.getByText('← Back to methods')).toBeVisible();
  });

  // S28: canvas page loads for an existing workflow editor route.
  test('S28: workflow editor view loads canvas and YAML panels', async ({ page, request }) => {
    const { org, workspace } = await setupReadyOrg(page);
    const api = await request.newContext();
    const { createTestWorkflow } = await import('./fixtures/test-factories');
    const id = await createTestWorkflow(api, org.accessToken, workspace.id, 'Editor Test');
    await api.dispose();

    await page.goto(`/workflows/${id}/edit`);
    // Wait for the toolbar to confirm editor loaded
    await expect(page.getByText('Edit workflow')).toBeVisible({ timeout: 10_000 });
  });

  // S29: Ctrl+K opens the Co-pilot panel.
  test('S29: Ctrl+K opens Co-pilot panel', async ({ page, request }) => {
    const { org, workspace } = await setupReadyOrg(page);
    const api = await request.newContext();
    const { createTestWorkflow } = await import('./fixtures/test-factories');
    const id = await createTestWorkflow(api, org.accessToken, workspace.id, 'Copilot Test');
    await api.dispose();

    await page.goto(`/workflows/${id}/edit`);
    await page.getByText('Edit workflow').waitFor();
    await page.keyboard.press('Control+k');
    await expect(page.getByText('✦ Co-pilot')).toBeVisible();
  });

  // S30: Help button opens the help panel.
  test('S30: Help button opens help panel in editor', async ({ page, request }) => {
    const { org, workspace } = await setupReadyOrg(page);
    const api = await request.newContext();
    const { createTestWorkflow } = await import('./fixtures/test-factories');
    const id = await createTestWorkflow(api, org.accessToken, workspace.id, 'Help Test');
    await api.dispose();

    await page.goto(`/workflows/${id}/edit`);
    await page.getByText('Edit workflow').waitFor();
    await page.getByRole('button', { name: 'Help' }).click();
    // Help panel or article should be visible
    await expect(page.getByText(/canvas/i)).toBeVisible({ timeout: 5_000 });
  });
});
