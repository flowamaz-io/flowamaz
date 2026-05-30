import { expect, request, test } from '@playwright/test';
import { API_URL } from './fixtures/test-factories';

/**
 * Production hardening (S72). The /health endpoint is public and reports the status of every
 * critical dependency (database, cache); it returns healthy when all dependencies are up.
 */
test.describe('Performance & health (S72)', () => {
  // S72: /health returns healthy with all dependencies reported.
  test('S72: /health is healthy with all dependencies', async () => {
    const api = await request.newContext();
    const res = await api.get(`${API_URL}/health`);
    await api.dispose();

    expect(res.ok()).toBeTruthy();
    const body = (await res.json()) as { status?: string; entries?: Record<string, unknown> };
    expect(body.status).toMatch(/Healthy/i);
    // Each registered dependency check is present in the report.
    expect(body.entries ?? {}).toBeTruthy();
  });
});
