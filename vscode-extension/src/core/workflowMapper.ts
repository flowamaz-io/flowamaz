/**
 * Pure mappers for the workflow tree view. No `vscode` import.
 * Turns raw API workflow objects into plain tree-node descriptors so the
 * mapping is unit-testable independently of the VS Code TreeDataProvider.
 */

export interface ApiWorkflow {
  id?: unknown;
  name?: unknown;
  status?: unknown;
  version?: unknown;
}

export interface WorkflowTreeNode {
  id: string;
  label: string;
  description: string;
  status: string;
}

function str(value: unknown, fallback = ""): string {
  if (typeof value === "string") {
    return value;
  }
  if (typeof value === "number" || typeof value === "boolean") {
    return String(value);
  }
  return fallback;
}

/**
 * Map an array of API workflow objects into tree-node descriptors. Skips
 * entries without an id. Tolerant of missing/extra fields.
 */
export function mapWorkflowsToTree(workflows: ApiWorkflow[]): WorkflowTreeNode[] {
  const nodes: WorkflowTreeNode[] = [];
  for (const wf of workflows) {
    const id = str(wf.id);
    if (id.length === 0) {
      continue;
    }
    const status = str(wf.status, "unknown");
    const version = str(wf.version);
    const description = version.length > 0 ? status + " · v" + version : status;
    nodes.push({
      id,
      label: str(wf.name, id),
      description,
      status,
    });
  }
  return nodes;
}
