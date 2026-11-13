import { expect, request, test } from '@playwright/test';
import { API_URL, setupReadyOrg } from './fixtures/test-factories';

// Fixes from the PR #22 CC test (S86–S87).

test.describe('Library install and CLI sign-in (S86–S87)', () => {
  // S86: installing a connector from the web is saved on the server (it used to exist only in the browser).
  test('S86: a web install of File System survives a reload', async ({ page }) => {
    const ready = await setupReadyOrg(page);
    await page.goto('/library/connectors/file-system');
    await page.getByRole('button', { name: /Install/ }).first().click();
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('button', { name: /Continue|Install/ }).first().click();
    await expect(dialog.getByText('Connected to File System!')).toBeVisible();
    await dialog.getByRole('button', { name: /Done|Finish/ }).click();

    // The server has the install, not just the browser.
    const api = await request.newContext();
    try {
      const res = await api.get(`${API_URL}/api/v1/workspaces/${ready.workspace.id}/library/connectors/file-system`, {
        headers: { Authorization: `Bearer ${ready.org.accessToken}` },
      });
      const body = (await res.json()) as { data: { connector: { installed: boolean; installedPin: string | null } } };
      expect(body.data.connector.installed).toBe(true);
      expect(body.data.connector.installedPin).toBe('flowamaz-io/file-system@1');
    } finally {
      await api.dispose();
    }
    await page.reload();
    await expect(page.getByText(/✓ Installed|Installed/).first()).toBeVisible();
  });

  // S87: `fmz auth login` opens /cli-auth?code=…; approving there completes the CLI's device flow.
  test('S87: /cli-auth approves a CLI device code', async ({ page }) => {
    await setupReadyOrg(page);
    const api = await request.newContext();
    try {
      const device = (await (await api.post(`${API_URL}/api/v1/cli/auth/device`)).json()) as {
        data?: { deviceCode: string; userCode: string; verificationUriComplete: string };
      } & { deviceCode?: string; userCode?: string; verificationUriComplete?: string };
      const flow = device.data ?? device;
      expect(flow.verificationUriComplete).toContain('/cli-auth?code=');

      const pending = await api.get(`${API_URL}/api/v1/cli/auth/token?deviceCode=${flow.deviceCode}`);
      expect(pending.status()).toBe(202);

      await page.goto(`/cli-auth?code=${flow.userCode}`);
      await expect(page.getByTestId('cli-auth-code')).toHaveValue(flow.userCode!);
      await page.getByTestId('cli-auth-approve').click();
      await expect(page.getByText('CLI signed in')).toBeVisible();

      const done = await api.get(`${API_URL}/api/v1/cli/auth/token?deviceCode=${flow.deviceCode}`);
      expect(done.status()).toBe(200);
      // The token itself is never printed or stored by the test.
    } finally {
      await api.dispose();
    }
  });
});
