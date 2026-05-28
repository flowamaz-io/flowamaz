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
  matched_pattern: string | null;
  yaml_patch: string | null;
  cache_hit: boolean;
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
    const { data } = await http.post<CopilotResponse>(
      `/api/v1/workspaces/${workspaceId}/workflows/${workflowId}/copilot`,
      { command, yaml_content: yamlContent },
    );
    return data;
  },
};
