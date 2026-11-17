import { describe, expect, it, vi } from 'vitest';
import { createEngineApiClient, describeError, WorkbenchApiError } from '../src';

function fakeFetch(status: number, body: unknown) {
  return vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) =>
    new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }));
}

describe('createEngineApiClient', () => {
  it('calls the workspace routes with the host headers', async () => {
    const fetch = fakeFetch(200, { items: [], total: 0, page: 2, pageSize: 10 });
    const api = createEngineApiClient({ baseUrl: '/api/v1/', workspaceId: 'ws-1', fetch, headers: () => ({ Authorization: 'Bearer t' }) });

    const page = await api.listWorkflows(2, 10);

    expect(page.page).toBe(2);
    const [url, init] = fetch.mock.calls[0]!;
    expect(url).toBe('/api/v1/workspaces/ws-1/workflows?page=2&pageSize=10');
    expect((init!.headers as Record<string, string>).Authorization).toBe('Bearer t');
  });

  it('lists the connectors the host registered, and leaves credentials and Library forms to the host', async () => {
    const fetch = fakeFetch(200, [{ connectorId: 'slack', displayName: 'Slack', manifestJson: '{}', isOfficial: true }]);
    const api = createEngineApiClient({ baseUrl: '/api/v1', workspaceId: 'ws', fetch });

    expect(await api.listConnectors!()).toEqual([{ connectorId: 'slack', displayName: 'Slack', manifestJson: '{}', isOfficial: true }]);
    expect(fetch.mock.calls[0]![0]).toBe('/api/v1/workspaces/ws/connectors');
    expect(api.listCredentials).toBeUndefined();
    expect(api.listLibraryForms).toBeUndefined();
  });

  it('sends a run payload as JSON and the idempotency key as a header', async () => {
    const fetch = fakeFetch(202, { instanceId: 'r1', status: 'Pending', triggeredAt: '2026-11-16T00:00:00Z' });
    const api = createEngineApiClient({ baseUrl: '/api/v1', workspaceId: 'ws', fetch });

    await api.startRun('wf/1', { payload: '{"amount":5}', idempotencyKey: 'k1' });

    const [url, init] = fetch.mock.calls[0]!;
    expect(url).toBe('/api/v1/workspaces/ws/workflows/wf%2F1/runs');
    expect(JSON.parse(init!.body as string)).toEqual({ payload: { amount: 5 }, isTest: false });
    expect((init!.headers as Record<string, string>)['Idempotency-Key']).toBe('k1');
  });

  it('refuses a payload that is not JSON before calling the server', async () => {
    const fetch = fakeFetch(202, {});
    const api = createEngineApiClient({ baseUrl: '/api/v1', workspaceId: 'ws', fetch });

    await expect(api.startRun('wf', { payload: '{oops' })).rejects.toMatchObject({ code: 'INVALID_JSON' });
    expect(fetch).not.toHaveBeenCalled();
  });

  it('turns an error response into a WorkbenchApiError with the server message and code', async () => {
    const api = createEngineApiClient({
      baseUrl: '/api/v1', workspaceId: 'ws',
      fetch: fakeFetch(409, { code: 'WORKFLOW_SLUG_TAKEN', message: "A workflow with slug 'x' already exists." }),
    });

    const error = await api.createWorkflow({ name: 'X', slug: 'x', yamlContent: 'y', createdByMethod: 'Canvas' }).catch((e) => e);

    expect(error).toBeInstanceOf(WorkbenchApiError);
    expect(error).toMatchObject({ code: 'WORKFLOW_SLUG_TAKEN', status: 409 });
    expect(describeError(error).message).toContain('already exists');
  });

  it('explains an error without a body and a network failure', async () => {
    const bare = createEngineApiClient({ baseUrl: '/api', workspaceId: 'ws', fetch: fakeFetch(500, undefined) });
    await expect(bare.getRun('r')).rejects.toThrow(/HTTP 500/);

    const offline = createEngineApiClient({ baseUrl: '/api', workspaceId: 'ws', fetch: vi.fn(async () => { throw new TypeError('fail'); }) });
    await expect(offline.getRun('r')).rejects.toMatchObject({ code: 'NETWORK_ERROR' });
  });

  it('treats 204 as success with no body', async () => {
    const api = createEngineApiClient({ baseUrl: '/api', workspaceId: 'ws', fetch: vi.fn(async () => new Response(null, { status: 204 })) });
    await expect(api.deleteWorkflow('wf')).resolves.toBeUndefined();
  });
});

describe('describeError', () => {
  it('never returns an empty message', () => {
    expect(describeError(undefined).message).toMatch(/unexpected error/);
    expect(describeError(new Error('Boom, retry')).message).toBe('Boom, retry');
  });
});
