import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';

export interface EmpathyIssue {
  nodeId: string;
  label: string;
  type: 'NoStatusUpdate' | 'LongWait' | 'MultipleEmails' | 'NoOutcomeNotification' | 'VisibilityGap';
  description: string;
  suggestion: string;
  severity: 'High' | 'Medium' | 'Low';
}

export interface EmpathyAnalysis {
  workflowDefinitionId: string;
  emailsSentToRequester: number;
  waitPeriodCount: number;
  statusUpdateCount: number;
  avgDaysToOutcome: number;
  visibilityGapHours: number;
  score: number;
  issues: EmpathyIssue[];
}

export const empathyService = {
  async getEmpathyAnalysis(workspaceId: string, workflowId: string, yamlContent?: string): Promise<EmpathyAnalysis> {
    const res = await http.post<ApiEnvelope<EmpathyAnalysis>>(
      `/api/v1/workspaces/${workspaceId}/workflows/${workflowId}/empathy`,
      { yamlContent: yamlContent ?? null },
    );
    return unwrap(res);
  },
};
