import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';

/** break = an input that really breaks the run; warning = only bad input does; info = a branch nothing took. */
export type TwinSeverity = 'break' | 'warning' | 'info';
export type TwinStatus = 'passed' | 'warnings' | 'breaks' | 'failed';

export interface TwinFinding {
  kind: 'no-route' | 'multiple-routes' | 'never-taken' | string;
  severity: TwinSeverity;
  nodeId: string;
  nodeLabel: string;
  title: string;
  explanation: string;
  fix: string;
  /** Only the values this finding's conditions read (and the gate decisions), never the whole payload. */
  example: Record<string, string>;
  alsoFailsWith: string[];
  cases: number;
  /** Found by replaying a recent real payload. Its values are hidden from people who can't see run data. */
  fromRealPayload: boolean;
}

export interface TwinRun {
  id: string;
  workflowDefinitionId: string;
  versionLabel: 'production' | 'draft' | string;
  trigger: 'manual' | 'scheduled' | string;
  status: TwinStatus;
  casesRun: number;
  routersTested: number;
  breakCount: number;
  warningCount: number;
  findings: TwinFinding[];
  errorMessage: string | null;
  durationMs: number;
  completedAt: string;
}

export interface TwinLatest {
  run: TwinRun | null;
  /** Published workflows are simulated automatically every day and after each publish. */
  hasProductionVersion: boolean;
}

export interface TwinSummary {
  workflowDefinitionId: string;
  status: TwinStatus;
  breakCount: number;
  warningCount: number;
  casesRun: number;
  versionLabel: string;
  completedAt: string;
}

// Workflow Twin (FUNCTIONAL.md §8.1): shadow simulation of a workflow against generated inputs. Nothing runs for real.
export const twinService = {
  async latest(workspaceId: string, workflowId: string): Promise<TwinLatest> {
    const res = await http.get<ApiEnvelope<TwinLatest>>(`/api/v1/workspaces/${workspaceId}/workflows/${workflowId}/twin`);
    return unwrap(res);
  },

  async run(workspaceId: string, workflowId: string): Promise<TwinRun> {
    const res = await http.post<ApiEnvelope<TwinRun>>(`/api/v1/workspaces/${workspaceId}/workflows/${workflowId}/twin`);
    return unwrap(res);
  },

  async summary(workspaceId: string): Promise<TwinSummary[]> {
    const res = await http.get<ApiEnvelope<TwinSummary[]>>(`/api/v1/workspaces/${workspaceId}/twin/summary`);
    return unwrap(res) ?? [];
  },
};

/** Badge text and colour for a Twin result (shared by the workflow list and the Twin tab). */
export function twinBadge(status: TwinStatus | undefined, breaks: number, warnings: number): {
  label: string;
  variant: 'primary' | 'amber' | 'danger' | 'slate';
} {
  switch (status) {
    case 'breaks':
      return { label: breaks === 1 ? '1 break' : `${breaks} breaks`, variant: 'danger' };
    case 'warnings':
      return { label: warnings === 1 ? '1 warning' : `${warnings} warnings`, variant: 'amber' };
    case 'passed':
      return { label: 'No breaks', variant: 'primary' };
    case 'failed':
      return { label: "Couldn't simulate", variant: 'slate' };
    default:
      return { label: 'Not simulated', variant: 'slate' };
  }
}
