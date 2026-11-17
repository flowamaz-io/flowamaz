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
import type { GateFormField } from './types';

/** A connector a step can use, as the canvas's step settings list it. */
export interface ConnectorSummary {
  /** The id a step's YAML uses (`config.connector`). */
  connectorId: string;
  displayName: string;
  /** The connector manifest as JSON: its operations and how it signs in. */
  manifestJson: string;
  /** Official connectors and installed ones are offered; others are left out of the list. */
  isOfficial?: boolean;
  isInstalled?: boolean;
}

/** A saved credential, by name. Its value is never returned. */
export interface CredentialSummary {
  id: string;
  /** The name a step's YAML refers to (`config.credential`). */
  name: string;
  connectorId: string | null;
  isActive: boolean;
}

/** A reusable approval form (fields an approver fills in), as a Library offers it. */
export interface LibraryFormSummary {
  id: string;
  slug: string;
  name: string;
  isOfficial: boolean;
  isWorkspaceForm?: boolean;
  publisher: string | null;
  fields: GateFormField[];
}

/** Filters for {@link WorkbenchApi.listRuns}. */
export interface RunListFilters {
  status?: string;
  workflowId?: string;
  includeTest?: boolean;
  page?: number;
  pageSize?: number;
}

/** Options for {@link WorkbenchApi.listGates}. */
export interface GateListOptions {
  /** Include gates of test runs (they come back with isTest = true). */
  includeTest?: boolean;
  /** Include decided gates (the "All" tab). */
  includeDecided?: boolean;
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

  /** Gates waiting for a decision; with includeDecided, decided ones too, newest first. */
  listGates(options?: GateListOptions): Promise<GateResponse[]>;
  decideGate(
    runId: string,
    nodeId: string,
    decision: 'approved' | 'rejected',
    note?: string,
    values?: Record<string, GateFormValue>,
  ): Promise<GateResponse>;

  // Used by the canvas's step settings. Optional: without them the settings still edit the YAML, they just can't
  // offer a list to pick from (connectors, credentials) or the Library (approval forms).
  /** Connectors a step can use. */
  listConnectors?(): Promise<ConnectorSummary[]>;
  /** Saved credentials, optionally for one connector. */
  listCredentials?(connectorId?: string): Promise<CredentialSummary[]>;
  /** Approval forms in the Library. */
  listLibraryForms?(): Promise<LibraryFormSummary[]>;
  /** Saves an approval form to the Library, so other gates can reuse it. */
  saveLibraryForm?(form: { name: string; fields: GateFormField[] }): Promise<LibraryFormSummary>;
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
