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
  VariableResponse,
  WorkflowDefinitionListItem,
  WorkflowDefinitionResponse,
  WorkflowVersionResponse,
} from './types';

/** Filters for {@link WorkbenchApi.listRuns}. */
export interface RunListFilters {
  status?: string;
  workflowId?: string;
  includeTest?: boolean;
  page?: number;
  pageSize?: number;
}

/** What to start a run with. `payload` is the run's trigger payload as JSON text. */
export interface StartRunRequest {
  payload?: string | null;
  isTest?: boolean;
  idempotencyKey?: string | null;
}

/**
 * Everything the workbench screens read and change, for one workspace. A host provides one: the Flowamaz engine API
 * ({@link createEngineApiClient}) or its own backend. Implementations throw {@link WorkbenchApiError} with an
 * actionable message when a call fails.
 */
export interface WorkbenchApi {
  listWorkflows(page?: number, pageSize?: number): Promise<Page<WorkflowDefinitionListItem>>;
  getWorkflow(id: string): Promise<WorkflowDefinitionResponse>;
  createWorkflow(request: CreateWorkflowDefinitionRequest): Promise<WorkflowDefinitionResponse>;
  saveWorkflow(id: string, yamlContent: string): Promise<WorkflowDefinitionResponse>;
  deleteWorkflow(id: string): Promise<void>;
  publishWorkflow(id: string): Promise<WorkflowVersionResponse>;
  archiveWorkflow(id: string): Promise<WorkflowDefinitionResponse>;
  unarchiveWorkflow(id: string): Promise<WorkflowDefinitionResponse>;
  listVersions(id: string): Promise<WorkflowVersionResponse[]>;

  startRun(workflowId: string, request?: StartRunRequest): Promise<TriggerInstanceResponse>;
  listRuns(filters?: RunListFilters): Promise<Page<InstanceListItem>>;
  getRun(id: string): Promise<InstanceDetailResponse>;
  listRunEvents(id: string): Promise<EventResponse[]>;
  /** The run's variables; sensitive values come back masked. */
  listRunVariables(id: string): Promise<VariableResponse[]>;
  getRunTimeline(id: string): Promise<TimelineResponse>;
  cancelRun(id: string): Promise<InstanceDetailResponse>;
  retryRun(id: string): Promise<TriggerInstanceResponse>;

  listPendingGates(includeTest?: boolean): Promise<GateResponse[]>;
  decideGate(
    runId: string,
    nodeId: string,
    decision: 'approved' | 'rejected',
    note?: string,
    values?: Record<string, GateFormValue>,
  ): Promise<GateResponse>;
}

/** A failed API call, with a message that says what happened and what to do next. */
export class WorkbenchApiError extends Error {
  constructor(
    message: string,
    /** The API's error code, e.g. WORKFLOW_SLUG_TAKEN. */
    readonly code?: string,
    /** The HTTP status, when there was a response. */
    readonly status?: number,
  ) {
    super(message);
    this.name = 'WorkbenchApiError';
  }
}

/** A user-facing message for anything thrown, never a bare "something went wrong". */
export function describeError(error: unknown): { message: string; code?: string; status?: number } {
  if (error instanceof WorkbenchApiError) return { message: error.message, code: error.code, status: error.status };
  if (error instanceof Error && error.message) return { message: error.message };
  return { message: 'An unexpected error occurred. Try again; if it keeps happening, check the server logs.', code: 'UNKNOWN' };
}
