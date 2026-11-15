import * as vscode from "vscode";
import axios from "axios";
import {
  ApiWorkflow,
  mapWorkflowsToTree,
  WorkflowTreeNode,
} from "../core/workflowMapper";

const REFRESH_INTERVAL_MS = 30000;

/**
 * Tree item representing a single workflow.
 */
class WorkflowItem extends vscode.TreeItem {
  constructor(node: WorkflowTreeNode) {
    super(node.label, vscode.TreeItemCollapsibleState.None);
    this.id = node.id;
    this.description = node.description;
    this.tooltip = node.label + " (" + node.status + ")";
    this.contextValue = "flowamazWorkflow";
    this.iconPath = new vscode.ThemeIcon("symbol-event");
    this.command = {
      command: "flowamaz.showInstanceLogs",
      title: "Show Instance Logs",
      arguments: [node.id],
    };
  }
}

/**
 * Message item shown when there are no workflows or credentials are missing.
 * Empty states teach: every message says what to do next.
 */
class MessageItem extends vscode.TreeItem {
  constructor(message: string) {
    super(message, vscode.TreeItemCollapsibleState.None);
    this.iconPath = new vscode.ThemeIcon("info");
  }
}

/**
 * TreeDataProvider listing the current workspace's workflows. API access is
 * isolated in `fetchWorkflows`; the tree is built from the pure
 * `mapWorkflowsToTree` mapper which is unit-tested separately.
 */
export class WorkflowTreeProvider implements vscode.TreeDataProvider<vscode.TreeItem> {
  private readonly emitter = new vscode.EventEmitter<void>();
  public readonly onDidChangeTreeData = this.emitter.event;

  private nodes: WorkflowTreeNode[] = [];
  private statusMessage: string | undefined;
  private timer: NodeJS.Timeout | undefined;

  public register(context: vscode.ExtensionContext): void {
    context.subscriptions.push(
      vscode.window.registerTreeDataProvider("flowamaz.workflows", this)
    );
    this.timer = setInterval(() => void this.refresh(), REFRESH_INTERVAL_MS);
    context.subscriptions.push({
      dispose: () => {
        if (this.timer) {
          clearInterval(this.timer);
        }
      },
    });
    void this.refresh();
  }

  public getTreeItem(element: vscode.TreeItem): vscode.TreeItem {
    return element;
  }

  public getChildren(): vscode.ProviderResult<vscode.TreeItem[]> {
    if (this.statusMessage) {
      return [new MessageItem(this.statusMessage)];
    }
    if (this.nodes.length === 0) {
      return [
        new MessageItem(
          "No workflows yet. Create one with the fmz CLI or the Flowamaz app."
        ),
      ];
    }
    return this.nodes.map((n) => new WorkflowItem(n));
  }

  public async refresh(): Promise<void> {
    const config = vscode.workspace.getConfiguration("flowamaz");
    const apiUrl = config.get<string>("apiUrl", "https://api.flowamaz.io");
    const workspace = config.get<string>("workspace", "").trim();
    const apiKey = process.env.FMZ_API_KEY ?? "";

    if (apiKey.length === 0) {
      this.statusMessage =
        "Set the FMZ_API_KEY environment variable to list workflows.";
      this.nodes = [];
      this.emitter.fire();
      return;
    }
    if (workspace.length === 0) {
      this.statusMessage =
        "Set flowamaz.workspace in settings (or run Flowamaz: Switch Workspace).";
      this.nodes = [];
      this.emitter.fire();
      return;
    }

    try {
      const workflows = await fetchWorkflows(apiUrl, workspace, apiKey);
      this.nodes = mapWorkflowsToTree(workflows);
      this.statusMessage = undefined;
    } catch (err) {
      const detail = err instanceof Error ? err.message : String(err);
      this.statusMessage =
        "Could not reach the Flowamaz API: " +
        detail +
        ". Check flowamaz.apiUrl and FMZ_API_KEY.";
      this.nodes = [];
    }
    this.emitter.fire();
  }
}

/**
 * Fetch workspace workflows from the Flowamaz API. Isolated so the rest of the
 * provider stays free of HTTP concerns.
 */
async function fetchWorkflows(
  apiUrl: string,
  workspace: string,
  apiKey: string
): Promise<ApiWorkflow[]> {
  const client = axios.create({
    baseURL: apiUrl,
    headers: {
      "Content-Type": "application/json",
      Authorization: "Bearer " + apiKey,
    },
    timeout: 15000,
    validateStatus: (status) => status >= 200 && status < 300,
  });
  const resp = await client.get(
    "/api/v1/workspaces/" + encodeURIComponent(workspace) + "/workflows"
  );
  const data = resp.data as unknown;
  if (Array.isArray(data)) {
    return data as ApiWorkflow[];
  }
  // AutoWrapper-style envelope: { result: [...] } or { data: [...] }.
  if (data && typeof data === "object") {
    const envelope = data as Record<string, unknown>;
    const inner = envelope.result ?? envelope.data;
    if (Array.isArray(inner)) {
      return inner as ApiWorkflow[];
    }
  }
  return [];
}
