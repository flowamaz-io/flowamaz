import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';

/** How a workflow performed over the last 30 days. */
export interface WorkflowPerformance {
  runs: number;
  successPercent: number;
  slaCompliancePercent: number;
  avgHours: number;
}

export type InheritanceKind = 'gate-timeout' | 'action-retry' | 'outcome-notification' | 'compensation';

/** A pattern a better-performing similar workflow uses and this one doesn't (§8.8). */
export interface InheritanceSuggestion {
  kind: InheritanceKind;
  fromWorkflowId: string;
  fromWorkflowName: string;
  similarity: number;
  evidence: string;
  title: string;
  description: string;
  canApply: boolean;
}

export interface InheritanceReport {
  workflowId: string;
  performance: WorkflowPerformance;
  suggestions: InheritanceSuggestion[];
  /** Explains an empty list. */
  message: string | null;
}

export const inheritanceService = {
  async getReport(workspaceId: string, workflowId: string): Promise<InheritanceReport> {
    const res = await http.get<ApiEnvelope<InheritanceReport>>(
      `/api/v1/workspaces/${workspaceId}/workflows/${workflowId}/inheritance`,
    );
    return unwrap(res);
  },

  /** Adds the pattern to this workflow and saves it; returns the new YAML. */
  async apply(workspaceId: string, workflowId: string, kind: InheritanceKind, fromWorkflowId: string): Promise<string> {
    const res = await http.post<ApiEnvelope<{ yamlContent: string }>>(
      `/api/v1/workspaces/${workspaceId}/workflows/${workflowId}/inheritance/apply`,
      { kind, fromWorkflowId },
    );
    return unwrap(res).yamlContent;
  },
};
