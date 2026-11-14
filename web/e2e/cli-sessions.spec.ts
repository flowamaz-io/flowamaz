import { expect, request, test } from '@playwright/test';
import { API_URL, setupReadyOrg } from './fixtures/test-factories';

// CLI sessions (S90–S91): `fmz auth login` gives the terminal a session of its own, Settings → CLI sessions lists it,
// and signing it out there cuts the terminal off at once. Tokens are only held in memory; nothing is printed or saved.

type Envelope<T> = { data?: T } & Partial<T>;
const body = async <T>(res: { json(): Promise<unknown> }): Promise<T> => {
  const json = (await res.json()) as Envelope<T>;
  return (json.data ?? json) as T;
};

test.describe('CLI sessions (S90–S91)', () => {
  test('S90: the approval page names the terminal, and the CLI gets its own refreshable session', async ({ page }) => {
    await setupReadyOrg(page);
    const api = await request.newContext();
    try {
      const flow = await body<{ deviceCode: string; userCode: string }>(
        await api.post(`${API_URL}/api/v1/cli/auth/device`, { data: { clientName: 'fmz on e2e-box' } }),
      );

      await page.goto(`/cli-auth?code=${flow.userCode}`);
      await expect(page.getByTestId('cli-auth-client')).toContainText('fmz on e2e-box');
      await page.getByTestId('cli-auth-approve').click();
      await expect(page.getByText('CLI signed in')).toBeVisible();
      await expect(page.getByTestId('cli-auth-sessions-link')).toBeVisible();

      const tokens = await body<{ accessToken: string; refreshToken: string }>(
        await api.get(`${API_URL}/api/v1/cli/auth/token?deviceCode=${flow.deviceCode}`),
      );
      expect(tokens.refreshToken).toBeTruthy();

      const refreshed = await api.post(`${API_URL}/api/v1/cli/auth/refresh`, { data: { refreshToken: tokens.refreshToken } });
      expect(refreshed.status()).toBe(200);
    } finally {
      await api.dispose();
    }
  });

  test('S91: Settings → CLI sessions lists the terminal and signing it out ends it at once', async ({ page }) => {
    await setupReadyOrg(page);
    const api = await request.newContext();
    try {
      const flow = await body<{ deviceCode: string; userCode: string }>(
        await api.post(`${API_URL}/api/v1/cli/auth/device`, { data: { clientName: 'fmz on e2e-revoke' } }),
      );
      await page.goto(`/cli-auth?code=${flow.userCode}`);
      await page.getByTestId('cli-auth-approve').click();
      await expect(page.getByText('CLI signed in')).toBeVisible();
      const tokens = await body<{ accessToken: string; refreshToken: string; sessionId: string }>(
        await api.get(`${API_URL}/api/v1/cli/auth/token?deviceCode=${flow.deviceCode}`),
      );
      const me = () => api.get(`${API_URL}/api/v1/auth/me`, { headers: { Authorization: `Bearer ${tokens.accessToken}` } });
      expect((await me()).status()).toBe(200);

      await page.goto('/settings/cli-sessions');
      const row = page.getByTestId('cli-session-row').filter({ hasText: 'fmz on e2e-revoke' });
      await expect(row).toBeVisible();
      await page.getByTestId(`cli-session-revoke-${tokens.sessionId}`).click();
      await page.getByTestId('cli-session-revoke-confirm').click();
      await expect(row).toHaveCount(0);

      expect((await me()).status()).toBe(401);
      const refresh = await api.post(`${API_URL}/api/v1/cli/auth/refresh`, { data: { refreshToken: tokens.refreshToken } });
      expect(refresh.status()).toBe(401);
    } finally {
      await api.dispose();
    }
  });
});
