import type { BadgeVariant } from './runs';
import type { WorkflowStatus } from './types';

// Helpers for showing workflows (workflowStatusVariant and healthColorClass moved from the product's
// src/utils/workflow.util.ts, which re-exports them).

/** Maps a workflow status to an FmBadge colour variant. */
export function workflowStatusVariant(status: WorkflowStatus): BadgeVariant {
  return status === 'Published' ? 'primary' : 'slate';
}

/** Health score colour band: 80-100 green, 60-79 amber, 0-59 red. */
export function healthColorClass(score: number): string {
  if (score >= 80) return 'text-teal-600';
  if (score >= 60) return 'text-amber-600';
  return 'text-danger-600';
}

/**
 * A small workflow that publishes as it is (a manual trigger, one approval, an end), so a new workflow can be
 * tried straight away and edited from there. The name is written as a quoted YAML string.
 */
export function starterWorkflowYaml(name: string, slug: string): string {
  const quoted = JSON.stringify(name);
  return `apiVersion: flowamaz/v1
kind: Workflow
metadata:
  id: ${slug}
  name: ${quoted}

spec:
  trigger:
    type: manual

  nodes:
    - id: start
      type: trigger
      label: "Started"

    - id: approve
      type: human-gate
      label: "Approval"
      config:
        assignee: approver@example.com
        gate_label: "Approve this request"

    - id: done
      type: end
      label: "Done"

  edges:
    - { id: e1, from: start, to: approve }
    - { id: e2, from: approve, to: done }
`;
}
