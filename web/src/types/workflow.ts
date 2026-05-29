// Workflow-engine DTOs — mirror the Phase 2 backend (camelCase, string enums).

export type WorkflowStatus = 'Draft' | 'Published' | 'Archived';

export type InstanceStatus =
  | 'Pending'
  | 'Running'
  | 'Waiting'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'
  | 'Compensating';

export type WorkflowCreatedByMethod =
  | 'NaturalLanguage'
  | 'Voice'
  | 'VisualInput'
  | 'Conversation'
  | 'Document'
  | 'Canvas';

export type WorkflowTriggerType = 'Webhook' | 'Form' | 'Schedule' | 'Manual' | 'SubWorkflow';

export type NarrativeAudience = 'ceo' | 'auditor' | 'developer';

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

export interface WorkflowCommit {
  sha: string;
  shortSha: string;
  message: string;
  authorName: string;
  authorEmail: string;
  committedAt: string;
}

export interface WorkflowDiffLine {
  type: 'added' | 'removed' | 'context';
  content: string;
  oldLineNumber: number | null;
  newLineNumber: number | null;
}

export interface WorkflowDiffSummary {
  addedNodes: string[];
  removedNodes: string[];
  modifiedNodes: string[];
}

export interface WorkflowDiff {
  fromSha: string;
  toSha: string;
  lines: WorkflowDiffLine[];
  summary: WorkflowDiffSummary;
}

export interface WorkflowAtCommitResponse {
  workflowId: string;
  commitSha: string;
  yamlContent: string;
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
  createdAt: string;
  isTest: boolean;
  testExpiresAt: string | null;
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
}

export interface InterpreterNarrative {
  audience: string;
  content: string;
  generatedAt: string;
}

export type WeatherStatus = 'green' | 'yellow' | 'orange' | 'red';

export interface WorkflowWeather {
  workflowId: string;
  workflowName: string;
  status: WeatherStatus;
  activeInstances: number;
  failedLastHour: number;
  slaCompliancePct: number;
  pendingInsights: number;
  latestInsight: string | null;
}

export interface WeatherResponse {
  generatedAt: string;
  totalWorkflows: number;
  activeInstances: number;
  runsThisMonth: number;
  workflows: WorkflowWeather[];
}

export interface WorkflowRoiDetail {
  workflowDefinitionId: string;
  workflowName: string;
  configured: boolean;
  runs: number;
  successfulRuns: number;
  timeSavedMinutes: number;
  costAvoided: number;
  roiPercentage: number;
  currency: string;
}

export interface WorkspaceRoiSummary {
  periodStart: string;
  periodEnd: string;
  totalRunsInPeriod: number;
  successfulRuns: number;
  totalTimeSavedMinutes: number;
  totalCostAvoided: number;
  roiPercentage: number;
  avgCostPerRun: number;
  byWorkflow: WorkflowRoiDetail[];
}

export interface RoiConfigDto {
  manualProcessTimeMinutes: number;
  manualProcessCostPerRunUsd: number;
  automationCostPerRunUsd: number;
  monthlyCurrency: string;
}

export interface InsightResponse {
  id: string;
  workflowDefinitionId: string;
  instanceId: string | null;
  insightType: string;
  severity: string;
  message: string;
  data: string;
  isAcknowledged: boolean;
  acknowledgedAt: string | null;
  expiresAt: string | null;
  createdAt: string;
}
