import { expect, request, test } from '@playwright/test';
import { API_URL, createTestOrg, createTestWorkspace } from './fixtures/test-factories';

interface Envelope<T> { data: T }
function unwrap<T>(body: { data: T }): T { return body.data; }

test.describe('Launch readiness (S79–S80)', () => {
  // S79: /health reports all five dependencies (database, redis, stripe, anthropic, resend).
  test('S79: GET /health reports all five dependencies', async () => {
    const api = await request.newContext();
    const res = await api.get(`${API_URL}/health`);
    // Critical deps up → 200; non-critical externals may be Degraded but never 503 the endpoint.
    expect(res.status()).toBe(200);

    const body = await res.json();
    const deps = body.dependencies ?? {};
    for (const name of ['database', 'redis', 'stripe', 'anthropic', 'resend']) {
      expect(deps).toHaveProperty(name);
    }
    expect(['Healthy', 'Degraded']).toContain(body.status);
    await api.dispose();
  });

  // S80: Install an official template → publish succeeds (verifies the fix-07 SfgParser Critical fix E2E).
  test('S80: install official template then publish succeeds', async () => {
    const api = await request.newContext();
    const org = await createTestOrg(api, {});
    const ws = await createTestWorkspace(api, org.accessToken);
    const auth = { Authorization: `Bearer ${org.accessToken}` };

    // Pick the first official template.
    const listRes = await api.get(`${API_URL}/api/v1/templates?pageSize=50`);
    const templates = unwrap<{ items: Array<{ id: string }> }>(await listRes.json() as Envelope<{ items: Array<{ id: string }> }>);
    const templateId = templates.items[0]!.id;

    // Install → creates a workflow from the template's flowamaz/v1 YAML.
    const installRes = await api.post(
      `${API_URL}/api/v1/workspaces/${ws.id}/templates/${templateId}/install`,
      { headers: auth, data: { name: 'E2E From Template' } },
    );
    expect(installRes.ok()).toBeTruthy();
    const workflowId = unwrap<{ workflowId: string }>(await installRes.json() as Envelope<{ workflowId: string }>).workflowId;

    // Publish → parses the YAML through SfgParser (the Critical fix). Must succeed.
    const publishRes = await api.post(
      `${API_URL}/api/v1/workspaces/${ws.id}/workflows/${workflowId}/publish`,
      { headers: auth },
    );
    expect(publishRes.ok()).toBeTruthy();
    await api.dispose();
  });
});
