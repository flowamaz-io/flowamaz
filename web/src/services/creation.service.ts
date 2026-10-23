import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';

export interface NlWorkflowRequest {
  workflowName: string;
  purpose: string;
  triggerDescription: string;
  stepsDescription: string;
  rulesAndConstraints: string;
  systemsAndAi: string;
  existingContext?: string;
  /** Answers to clarifying questions from POST /workflows/clarify. Skipped questions are omitted. */
  clarifications?: ClarificationAnswer[];
}

export type ClarificationTopic = 'trigger' | 'approver' | 'rejection' | 'systems' | 'other';

export interface ClarifyingQuestion {
  id: string;
  topic: ClarificationTopic;
  question: string;
  hint?: string | null;
}

export interface ClarificationAnswer {
  topic: ClarificationTopic;
  question: string;
  answer: string;
}

export interface ClarificationResult {
  questions: ClarifyingQuestion[];
  usedAi: boolean;
  tokensUsed: number;
}

export interface DetectedNode {
  id: string;
  label: string;
  type: string;
  confidence: number;
  reviewNeeded: boolean;
  reason: string | null;
  annotations: string[];
  suggestedConnectors: string[];
  x?: number | null;
  y?: number | null;
}

export interface DetectedEdge {
  from: string;
  to: string;
  label?: string | null;
}

export interface VisualPreviewResult {
  nodes: DetectedNode[];
  edges: DetectedEdge[];
  confidenceThreshold: number;
  costUsd: number;
  tokensUsed: number;
}

export interface ConfirmedVisualNode {
  id: string;
  label: string;
  type: string;
  suggestedConnectors?: string[];
}

export interface ConfirmedVisualGraph {
  nodes: ConfirmedVisualNode[];
  edges: DetectedEdge[];
}

export interface ValidationIssue {
  layer: number;
  code: string;
  message: string;
  nodeId?: string;
  line?: number;
  column?: number;
}

export interface ValidationResult {
  isValid: boolean;
  errors: ValidationIssue[];
  warnings: ValidationIssue[];
  info: ValidationIssue[];
}

export interface UnifiedWorkflowCreateRequest {
  name: string;
  method: 'nl' | 'voice' | 'visual' | 'conversation' | 'document' | 'canvas';
  nlRequest?: NlWorkflowRequest;
  imageBase64?: string;
  imageMimeType?: string;
  conversationText?: string;
  sourceType?: string;
  documentBase64?: string;
  documentMimeType?: string;
  /** Visual review path: the graph confirmed by the user (replaces imageBase64). */
  visualGraph?: ConfirmedVisualGraph;
}

export interface WorkflowCreationResult {
  workflowId: string;
  workflowName: string;
  workflowSlug: string;
  method: string;
  validationResult?: ValidationResult;
  tokensUsed?: number;
  cached?: boolean;
}

export interface GenerationDoneEvent {
  yamlContent: string;
  validationResult: ValidationResult;
  tokensUsed: number;
  cached: boolean;
}

export interface CopilotResponse {
  matchedPattern: string | null;
  yamlPatch: string | null;
  cacheHit: boolean;
}

export interface SopParseResult {
  yaml_content: string;
  extracted_steps: string[];
  page_count: number;
  word_count: number;
  tokens_used: number;
}

export async function createWorkflow(
  workspaceId: string,
  request: UnifiedWorkflowCreateRequest,
): Promise<WorkflowCreationResult> {
  const response = await http.post<ApiEnvelope<WorkflowCreationResult>>(
    `/api/v1/workspaces/${workspaceId}/workflows/create`,
    request,
  );
  return unwrap(response);
}

/** Visual creation step 1 — detect the graph for review. Nothing is saved. */
export async function previewVisual(
  workspaceId: string,
  imageBase64: string,
  imageMimeType: string,
): Promise<VisualPreviewResult> {
  const response = await http.post<ApiEnvelope<VisualPreviewResult>>(
    `/api/v1/workspaces/${workspaceId}/workflows/visual/preview`,
    { imageBase64, imageMimeType },
  );
  return unwrap(response);
}

/** NL creation step 1 — up to 3 questions about missing essentials (empty = generate now). */
export async function clarifyNl(
  workspaceId: string,
  request: NlWorkflowRequest,
): Promise<ClarificationResult> {
  const response = await http.post<ApiEnvelope<ClarificationResult>>(
    `/api/v1/workspaces/${workspaceId}/workflows/clarify`,
    request,
  );
  return unwrap(response);
}

export const creationService = {
  async generateWorkflow(
    workspaceId: string,
    request: NlWorkflowRequest,
  ): Promise<GenerationDoneEvent> {
    const response = await http.post<ApiEnvelope<GenerationDoneEvent>>(
      `/api/v1/workspaces/${workspaceId}/workflows/generate`,
      request,
    );
    return unwrap(response);
  },

  async sendCopilotCommand(
    workspaceId: string,
    workflowId: string,
    command: string,
    yamlContent?: string,
  ): Promise<CopilotResponse> {
    const res = await http.post<ApiEnvelope<CopilotResponse>>(
      `/api/v1/workspaces/${workspaceId}/workflows/${workflowId}/copilot`,
      { command, yamlContent },
    );
    return unwrap(res);
  },
};
