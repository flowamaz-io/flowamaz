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
  /** What finished, non-test runs of the last 30 days showed; null when there were none. */
  runs?: EmpathyRunEvidence | null;
}

export interface EmpathyRunEvidence {
  runsAnalysed: number;
  avgDaysToOutcome: number;
  avgMessagesPerRun: number;
  /** Median over runs of the longest stretch without any message. */
  medianLongestSilenceHours: number;
  /** 0–100 */
  percentTimeWaitingOnApprovals: number;
  /** 0–100 */
  percentRunsOutcomeNotified: number;
  slowestGateNodeId: string | null;
  slowestGateAvgHours: number;
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
