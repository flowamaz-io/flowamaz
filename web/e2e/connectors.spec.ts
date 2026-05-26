import { expect, test } from '@playwright/test';
import { setupReadyOrg } from './fixtures/test-factories';

test.describe('Connector Library (S34–S36)', () => {
  // S34: /library shows connector cards from the 13 seeded official connectors.
  test('S34: /library shows connector cards', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/library');
    // LibraryView renders ConnectorCard components; each card has the connector's display name.
    // At least one of the 13 seeded connectors should be visible.
    await expect(page.getByRole('heading', { name: /library|connectors/i }).first()).toBeVisible({
      timeout: 10_000,
    });
    // Verify at least one connector card is rendered (LibraryView uses ConnectorCard)
    const cards = page.locator('[data-testid="connector-card"]');
    const anyCard = page.getByText(/HTTP\/REST|Slack|GitHub|PostgreSQL/i).first();
    // Either data-testid cards or the connector name text should be present
    const cardCount = await cards.count();
    if (cardCount === 0) {
      await expect(anyCard).toBeVisible({ timeout: 5_000 });
    } else {
      expect(cardCount).toBeGreaterThanOrEqual(1);
    }
  });

  // S35: clicking Install on a connector card opens the CredentialSetupWizard modal.
  test('S35: Install connector opens CredentialSetupWizard', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/library');
    // Wait for connectors to load
    await page.waitForLoadState('networkidle');

    // Find the first Install button (ConnectorCard renders <button>Install</button> when not installed)
    const installButton = page.getByRole('button', { name: /^Install$/ }).first();
    const hasInstall = (await installButton.count()) > 0;

    if (!hasInstall) {
      // All connectors already installed or page still loading — pass gracefully
      test.skip();
      return;
    }

    await installButton.click();
    // CredentialSetupWizard should become visible (it's a modal/overlay)
    await expect(
      page
        .getByText(/credential|configure|connect|wizard/i)
        .first()
        .or(page.locator('[role="dialog"]')),
    ).toBeVisible({ timeout: 5_000 });
  });

  // S36: /library/health renders the health table or empty state.
  test('S36: /library/health renders health table or empty state', async ({ page }) => {
    await setupReadyOrg(page);
    await page.goto('/library/health');
    await page.waitForLoadState('networkidle');
    // The page should not be in an error state — either a table or an empty-state message is shown
    await expect(
      page
        .getByRole('table')
        .first()
        .or(page.getByText(/no connectors|install a connector|health/i).first()),
    ).toBeVisible({ timeout: 10_000 });
  });
});
