import { expect, request, test } from '@playwright/test';
import { API_URL, setupReadyOrg, uniqueSuffix } from './fixtures/test-factories';

// Library publishers (FUNCTIONAL.md §23.9), S88–S89. The staff review page needs a staff org configured on the
// API, which this stack doesn't have; the integration tests cover it.

test.describe('Library publishers (S88–S89)', () => {
  // S88: a connector's publisher link opens the publisher's profile with everything they publish.
  test('S88: the official publisher profile lists its items', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/library');
    await page.getByTestId('connector-publisher-link').first().click();
    await expect(page).toHaveURL(/\/library\/publishers\/flowamaz-io$/);
    const profile = page.getByTestId('publisher-profile');
    await expect(profile.getByRole('heading', { name: 'Flowamaz' })).toBeVisible();
    await expect(profile.getByText('Official', { exact: true })).toBeVisible();
    for (const kind of ['connector', 'template', 'form', 'pack']) {
      await expect(page.getByTestId(`publisher-items-${kind}`)).toBeVisible();
    }
    await page.goto('/library/publishers/no-such-publisher-x');
    await expect(page.getByText('No such publisher')).toBeVisible();
  });

  // S89: the org owner fills in their Library profile and sees a template they published waiting for review.
  test('S89: the portal edits the profile and tracks a published template', async ({ page }) => {
    const ready = await setupReadyOrg(page);
    const title = `E2E vendor intake ${uniqueSuffix()}`;
    const api = await request.newContext();
    try {
      const manifest = [
        'kind: template', `name: ${title}`, 'version: 0.1.0', 'description: Onboard a vendor', 'workflow:',
        '  apiVersion: flowamaz/v1', '  kind: Workflow', '  metadata: { id: vendor-intake, name: Vendor intake, version: 1.0.0 }',
        '  spec:', '    trigger: { type: manual }', '    nodes:', '      - { id: start, type: trigger, label: Start }',
        '      - { id: done, type: end, label: Done }', '    edges:', '      - { id: e1, from: start, to: done }',
      ].join('\n');
      const res = await api.post(`${API_URL}/api/v1/workspaces/${ready.workspace.id}/library/templates/publish`, {
        headers: { Authorization: `Bearer ${ready.org.accessToken}` }, data: { manifest },
      });
      expect(res.status()).toBe(200);
    } finally {
      await api.dispose();
    }

    await page.goto('/settings/publisher');
    const section = page.getByTestId('library-publisher');
    await expect(section).toBeVisible();
    await section.getByTestId('library-publisher-edit').click();
    const form = section.getByTestId('library-publisher-form');
    await form.getByLabel('Display name').fill('E2E Labs');
    await form.getByLabel('Description').fill('Connectors for testing.');
    await form.getByLabel('Website').fill('https://e2e.example');
    await section.getByTestId('library-publisher-save').click();
    await expect(section.getByText('Profile saved.')).toBeVisible();

    const submission = section.getByTestId('library-submission-template').filter({ hasText: title });
    await expect(submission).toContainText('In review');
    await expect(submission).toContainText('Waiting for review');

    await section.getByTestId('library-publisher-public-link').click();
    await expect(page.getByTestId('publisher-profile').getByRole('heading', { name: 'E2E Labs' })).toBeVisible();
    await expect(page.getByText('Connectors for testing.')).toBeVisible();
    await expect(page.getByText(title)).toHaveCount(0);
  });
});
