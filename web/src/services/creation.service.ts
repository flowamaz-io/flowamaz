import { http } from './api.service';

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

export interface GenerationDoneEvent {
  yaml_content: string;
  validation_result: ValidationResult;
  tokens_used: number;
  cached: boolean;
}

export interface CopilotResponse {
  matched_pattern: string | null;
  yaml_patch: string | null;
  cache_hit: boolean;
}

export interface ConversationImportResult {
  yaml_content: string;
  extracted_process: {
    summary: string;
    steps: string[];
    approvers: string[];
    systems: string[];
  };
  confidence_score: number;
  tokens_used: number;
}

export interface SopParseResult {
  yaml_content: string;
  extracted_steps: string[];
  page_count: number;
  word_count: number;
  tokens_used: number;
}

export const creationService = {
  async generateWorkflow(
    workspaceId: string,
    request: NlWorkflowRequest,
  ): Promise<GenerationDoneEvent> {
    const { data } = await http.post<GenerationDoneEvent>(
      `/api/v1/workspaces/${workspaceId}/workflows/generate`,
      request,
    );
    return data;
  },

  async parseDocument(workspaceId: string, file: File): Promise<SopParseResult> {
    const form = new FormData();
    form.append('document', file);
    const { data } = await http.post<SopParseResult>(
      `/api/v1/workspaces/${workspaceId}/workflows/from-document`,
      form,
      { headers: { 'Content-Type': 'multipart/form-data' } },
    );
    return data;
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
