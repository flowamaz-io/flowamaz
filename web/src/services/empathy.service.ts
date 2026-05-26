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
  async getEmpathyAnalysis(workspaceId: string, workflowId: string): Promise<EmpathyAnalysis> {
    const res = await http.get<ApiEnvelope<EmpathyAnalysis>>(
      `/api/v1/workspaces/${workspaceId}/workflows/${workflowId}/empathy`,
    );
    return unwrap(res);
  },
};
