import { WorkbenchApiError, type ConnectorSummary, type GateListOptions, type RunListFilters, type StartRunRequest, type WorkbenchApi } from './api';
import type {
  CreateWorkflowDefinitionRequest,
  EventResponse,
  GateFormValue,
  GateResponse,
  InstanceDetailResponse,
  InstanceListItem,
  Page,
  TimelineResponse,
  TriggerInstanceResponse,
  WorkflowDefinitionListItem,
  WorkflowDefinitionResponse,
  WorkflowVersionResponse,
} from './types';

export interface EngineApiClientOptions {
  /** Where the engine API is mapped, e.g. "/api/v1" or "https://flowamaz.example.com/api/v1". */
  baseUrl: string;
  workspaceId: string;
  /** Extra headers on every call, e.g. an Authorization header. Called per request so a refreshed token is used. */
  headers?: () => Record<string, string> | Promise<Record<string, string>>;
  /** Defaults to the global fetch; credentials (cookies) are sent with same-origin requests. */
  fetch?: typeof fetch;
}

/** A {@link WorkbenchApi} for the Flowamaz engine API (the Flowamaz.Engine.Api NuGet package). */
export function createEngineApiClient(options: EngineApiClientOptions): WorkbenchApi {
  const doFetch = options.fetch ?? ((input: RequestInfo | URL, init?: RequestInit) => fetch(input, init));
  const base = `${options.baseUrl.replace(/\/+$/, '')}/workspaces/${encodeURIComponent(options.workspaceId)}`;

  async function call<T>(method: string, path: string, body?: unknown, extra?: Record<string, string>): Promise<T> {
    const headers: Record<string, string> = { Accept: 'application/json', ...(await options.headers?.()), ...extra };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    let response: Response;
    try {
      response = await doFetch(`${base}${path}`, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body),
        credentials: 'same-origin',
      });
    } catch {
      throw new WorkbenchApiError(
        'Could not reach the Flowamaz server. Check your connection and that the server is running, then try again.',
        'NETWORK_ERROR',
      );
    }
    if (response.status === 204) return undefined as T;
    const text = await response.text();
    let data: unknown = undefined;
    try { data = text ? JSON.parse(text) : undefined; } catch { /* not JSON */ }
    if (!response.ok) {
      const problem = (data ?? {}) as { code?: unknown; message?: unknown };
      const message = typeof problem.message === 'string' && problem.message
        ? problem.message
        : `The server answered HTTP ${response.status} without an explanation. Try again; if it keeps happening, check the server logs.`;
      throw new WorkbenchApiError(message, typeof problem.code === 'string' ? problem.code : undefined, response.status);
    }
    return data as T;
  }

  const query = (params: Record<string, string | number | boolean | undefined>): string => {
    const q = new URLSearchParams();
    for (const [key, value] of Object.entries(params)) if (value !== undefined && value !== '') q.set(key, String(value));
    const text = q.toString();
    return text ? `?${text}` : '';
  };
  const id = (value: string): string => encodeURIComponent(value);

  return {
    listWorkflows: (page, pageSize) => call<Page<WorkflowDefinitionListItem>>('GET', `/workflows${query({ page, pageSize })}`),
    getWorkflow: (wf) => call<WorkflowDefinitionResponse>('GET', `/workflows/${id(wf)}`),
    createWorkflow: (request: CreateWorkflowDefinitionRequest) => call<WorkflowDefinitionResponse>('POST', '/workflows', {
      name: request.name, slug: request.slug, yamlContent: request.yamlContent, description: request.nlDescription ?? null,
    }),
    saveWorkflow: (wf, yamlContent) => call<WorkflowDefinitionResponse>('PUT', `/workflows/${id(wf)}`, { yamlContent }),
    deleteWorkflow: (wf) => call<void>('DELETE', `/workflows/${id(wf)}`),
    publishWorkflow: (wf) => call<WorkflowVersionResponse>('POST', `/workflows/${id(wf)}/publish`),
    archiveWorkflow: (wf) => call<WorkflowDefinitionResponse>('POST', `/workflows/${id(wf)}/archive`),
    unarchiveWorkflow: (wf) => call<WorkflowDefinitionResponse>('POST', `/workflows/${id(wf)}/unarchive`),
    listVersions: (wf) => call<WorkflowVersionResponse[]>('GET', `/workflows/${id(wf)}/versions`),

    startRun: async (wf, request: StartRunRequest = {}) => {
      let payload: unknown = undefined;
      if (request.payload) {
        try { payload = JSON.parse(request.payload); }
        catch { throw new WorkbenchApiError('The run payload isn\'t valid JSON. Fix it (e.g. {"amount": 100}) and start the run again.', 'INVALID_JSON'); }
      }
      return call<TriggerInstanceResponse>('POST', `/workflows/${id(wf)}/runs`, { payload, isTest: request.isTest ?? false },
        request.idempotencyKey ? { 'Idempotency-Key': request.idempotencyKey } : undefined);
    },
    listRuns: (filters: RunListFilters = {}) => call<Page<InstanceListItem>>('GET', `/runs${query({
      status: filters.status, workflowId: filters.workflowId, includeTest: filters.includeTest, page: filters.page, pageSize: filters.pageSize,
    })}`),
    getRun: (run) => call<InstanceDetailResponse>('GET', `/runs/${id(run)}`),
    // The engine API returns variables (sensitive ones masked) with the run's detail.
    listRunVariables: async (run) => (await call<InstanceDetailResponse>('GET', `/runs/${id(run)}`)).variables,
    listRunEvents: (run) => call<EventResponse[]>('GET', `/runs/${id(run)}/events`),
    getRunTimeline: (run) => call<TimelineResponse>('GET', `/runs/${id(run)}/timeline`),
    cancelRun: (run) => call<InstanceDetailResponse>('POST', `/runs/${id(run)}/cancel`),
    retryRun: (run) => call<TriggerInstanceResponse>('POST', `/runs/${id(run)}/retry`),

    listGates: (options: GateListOptions = {}) => call<GateResponse[]>('GET', `/gates${query({
      includeTest: options.includeTest, status: options.includeDecided ? 'all' : undefined,
    })}`),
    decideGate: (run, nodeId, decision, note?: string, values?: Record<string, GateFormValue>) =>
      call<GateResponse>('POST', `/gates/${id(run)}/${id(nodeId)}/decide`, { decision, note, values }),

    // Credentials and Library forms are the host's own (the engine API has none), so the host adds those calls.
    listConnectors: () => call<ConnectorSummary[]>('GET', '/connectors'),
  };
}
