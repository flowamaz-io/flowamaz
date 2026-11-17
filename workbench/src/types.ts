// Workflow-engine DTOs shared by the Flowamaz engine API (Flowamaz.Engine.Api) and the Flowamaz product's API:
// camelCase, string enums. Moved from the product (web/src/types/workflow.ts), which re-exports them.

export type WorkflowStatus = 'Draft' | 'Published' | 'Archived';

export type InstanceStatus =
  | 'Pending'
  | 'Running'
  | 'Waiting'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'
  | 'Compensating'
  /** Ended by an approver's "no" at a gate with no rejected path: an outcome, not a failure. */
  | 'Rejected';

export type WorkflowCreatedByMethod =
  | 'NaturalLanguage'
  | 'Voice'
  | 'VisualInput'
  | 'Conversation'
  | 'Document'
  | 'Canvas';

export type WorkflowTriggerType = 'Webhook' | 'Form' | 'Schedule' | 'Manual' | 'SubWorkflow';

export interface WorkflowDefinitionListItem {
  id: string;
  name: string;
  slug: string;
  status: WorkflowStatus;
  healthScore: number;
  updatedAt: string;
}

export interface WorkflowDefinitionResponse {
  id: string;
  workspaceId: string;
  name: string;
  slug: string;
  description: string | null;
  yamlContent: string;
  nlDescription: string | null;
  createdByMethod: WorkflowCreatedByMethod;
  status: WorkflowStatus;
  currentVersion: string;
  healthScore: number;
  triggerType: WorkflowTriggerType;
  createdAt: string;
  updatedAt: string;
}

export interface WorkflowVersionResponse {
  id: string;
  workflowDefinitionId: string;
  commitSha: string;
  tagName: string | null;
  branchName: string;
  message: string;
  isProduction: boolean;
  createdAt: string;
}

export interface CreateWorkflowDefinitionRequest {
  name: string;
  slug: string;
  yamlContent: string;
  nlDescription?: string | null;
  createdByMethod: WorkflowCreatedByMethod;
}

export interface UpdateWorkflowDefinitionRequest {
  yamlContent: string;
}

export interface TriggerInstanceRequest {
  workflowDefinitionId: string;
  payload?: string | null;
  idempotencyKey?: string | null;
  isTest?: boolean;
}

export interface TriggerInstanceResponse {
  instanceId: string;
  status: InstanceStatus;
  triggeredAt: string;
}

export interface InstanceListItem {
  id: string;
  workflowDefinitionId: string;
  status: InstanceStatus;
  triggerType: string;
  startedAt: string | null;
  completedAt: string | null;
  /** Set when the run failed or was rejected (completedAt is only set on success). */
  failedAt?: string | null;
  createdAt: string;
  isTest: boolean;
  testExpiresAt: string | null;
  /** Who started the run (null for scheduled and webhook runs). */
  triggeredByUserId?: string | null;
}

export interface NodeStateResponse {
  nodeId: string;
  nodeType: string;
  status: string;
  startedAt: string | null;
  completedAt: string | null;
  retryCount: number;
  errorMessage: string | null;
}

export interface VariableResponse {
  name: string;
  value: string;
  isSensitive: boolean;
}

export interface EventResponse {
  sequenceNumber: number;
  eventType: string;
  nodeId: string | null;
  nodeType: string | null;
  payload: string;
  occurredAt: string;
  inputSnapshot: string | null;
  outputSnapshot: string | null;
  errorSnapshot: string | null;
  durationMs: number | null;
}

export interface InstanceDetailResponse {
  id: string;
  workflowDefinitionId: string;
  workflowVersionId: string;
  status: InstanceStatus;
  triggerType: string;
  correlationId: string | null;
  startedAt: string | null;
  completedAt: string | null;
  failedAt: string | null;
  errorMessage: string | null;
  currentNodeId: string | null;
  nodeStates: NodeStateResponse[];
  variables: VariableResponse[];
  events: EventResponse[];
  triggeredByUserId?: string | null;
  /** True for Viewers: variables, payloads and step inputs/outputs are left out (FUNCTIONAL.md §4). */
  dataHidden?: boolean;
  /** A test run (a draft's run, kept apart from production runs). */
  isTest?: boolean;
}

export interface TimelineNode {
  nodeId: string;
  nodeType: string;
  label: string;
  status: string;
  startedAt: string | null;
  completedAt: string | null;
  durationMs: number;
  offsetMs: number;
  retryCount: number;
  hasOutput: boolean;
}

export interface TimelineResponse {
  instanceId: string;
  totalDurationMs: number;
  startedAt: string | null;
  completedAt: string | null;
  nodes: TimelineNode[];
}

/** One field an approver fills in at a human gate (FUNCTIONAL.md §23.2). Mirrors Flowamaz.Core.Workflow.GateFormField. */
export type GateFormFieldType = 'text' | 'textarea' | 'number' | 'date' | 'select' | 'checkbox' | 'email';

export interface GateFormField {
  key: string;
  label: string;
  type: GateFormFieldType;
  required?: boolean;
  options?: string[] | null;
  help?: string | null;
  min?: number | null;
  max?: number | null;
}

export type GateFormValue = string | number | boolean;

export interface GateResponse {
  id: string;
  instanceId: string;
  nodeId: string;
  decision: string;
  assignedToEmail: string | null;
  deliveryChannel: string;
  deliveryStatus: string;
  expiresAt: string | null;
  createdAt: string;
  decidedAt: string | null;
  decisionNote: string | null;
  /** Who decided, as named by an external application through the public API (`decided_by`). */
  decidedByLabel?: string | null;
  /** Optional display label of the gate node (not sent by every API version). */
  gateLabel?: string | null;
  label?: string | null;
  /** True when the gate belongs to a test run (optional — older APIs omit it). */
  isTest?: boolean;
  /** Fields the approver fills in before approving (null when the gate has no form). */
  form?: GateFormField[] | null;
  /** What the approver entered, once approved. */
  formValues?: Record<string, GateFormValue> | null;
}

/** One page of a list. */
export interface Page<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}
