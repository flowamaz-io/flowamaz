import axios from 'axios';

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
  type: 'done';
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

export const creationService = {
  async generateWorkflow(
    workspaceId: string,
    request: NlWorkflowRequest,
    onChunk: (line: string) => void,
    signal?: AbortSignal,
  ): Promise<GenerationDoneEvent> {
    const response = await fetch(`/api/v1/workspaces/${workspaceId}/workflows/generate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
      body: JSON.stringify(request),
      signal,
    });

    if (!response.ok) throw new Error(`Generation failed with status ${response.status}. Please check your input and try again.`);
    if (!response.body) throw new Error('Streaming not supported by this environment.');

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split('\n');
      buffer = lines.pop() ?? '';

      for (const line of lines) {
        if (!line.startsWith('data: ')) continue;
        const data = JSON.parse(line.slice(6));
        if (data.type === 'chunk') onChunk(data.content);
        if (data.type === 'done') return data as GenerationDoneEvent;
        if (data.type === 'error') throw new Error(data.message);
      }
    }

    throw new Error('Stream ended unexpectedly. Please try again.');
  },

  async sendCopilotCommand(
    workspaceId: string,
    workflowId: string,
    command: string,
    yamlContent?: string,
  ): Promise<CopilotResponse> {
    const { data } = await axios.post<CopilotResponse>(
      `/api/v1/workspaces/${workspaceId}/workflows/${workflowId}/copilot`,
      { command, yaml_content: yamlContent },
    );
    return data;
  },
};
