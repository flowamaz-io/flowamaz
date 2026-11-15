import * as vscode from "vscode";
import { FlowamazHoverProvider } from "./providers/hover";
import { FlowamazCompletionProvider } from "./providers/completion";
import { FlowamazCodeLensProvider } from "./providers/codelens";
import { FlowamazDiagnostics } from "./providers/diagnostics";
import { WorkflowTreeProvider } from "./views/instanceTree";
import { triggerWorkflow } from "./commands/trigger";
import { validateWorkflow } from "./commands/validate";
import { openInApp } from "./commands/openInApp";

const YAML_SELECTOR: vscode.DocumentSelector = { language: "yaml" };

/**
 * Extension entry point. Registers language providers, the workflow tree view,
 * a status bar item, and the Flowamaz commands.
 */
export function activate(context: vscode.ExtensionContext): void {
  // Language providers (gated to Flowamaz workflows inside each provider).
  context.subscriptions.push(
    vscode.languages.registerHoverProvider(
      YAML_SELECTOR,
      new FlowamazHoverProvider()
    ),
    vscode.languages.registerCompletionItemProvider(
      YAML_SELECTOR,
      new FlowamazCompletionProvider()
    ),
    vscode.languages.registerCodeLensProvider(
      YAML_SELECTOR,
      new FlowamazCodeLensProvider()
    )
  );

  // Diagnostics (graph analysis + fmz CLI on save).
  const diagnostics = new FlowamazDiagnostics();
  diagnostics.register(context);

  // Workflow tree view (activity bar).
  const tree = new WorkflowTreeProvider();
  tree.register(context);

  // Status bar item.
  const status = vscode.window.createStatusBarItem(
    vscode.StatusBarAlignment.Left,
    100
  );
  status.text = "$(symbol-event) Flowamaz";
  status.tooltip = "Flowamaz workflow tools";
  status.command = "flowamaz.openInApp";
  status.show();
  context.subscriptions.push(status);

  // Commands.
  context.subscriptions.push(
    vscode.commands.registerCommand("flowamaz.triggerWorkflow", (uri?: vscode.Uri) =>
      triggerWorkflow(uri)
    ),
    vscode.commands.registerCommand("flowamaz.validateWorkflow", (uri?: vscode.Uri) =>
      validateWorkflow(uri)
    ),
    vscode.commands.registerCommand("flowamaz.openInApp", () => openInApp()),
    vscode.commands.registerCommand("flowamaz.refreshWorkflows", () => tree.refresh()),
    vscode.commands.registerCommand(
      "flowamaz.showInstanceLogs",
      (workflowId?: string) => showInstanceLogs(workflowId)
    ),
    vscode.commands.registerCommand("flowamaz.switchWorkspace", () =>
      switchWorkspace()
    )
  );
}

export function deactivate(): void {
  // Subscriptions disposed by VS Code.
}

/**
 * Open a terminal tailing instance logs for a workflow via the fmz CLI.
 */
function showInstanceLogs(workflowId?: string): void {
  const terminal = vscode.window.createTerminal("Flowamaz");
  terminal.show();
  if (workflowId && workflowId.length > 0) {
    terminal.sendText(
      "fmz instance list --workflow " + workflowId
    );
  } else {
    terminal.sendText("fmz instance list");
  }
}

/**
 * Prompt for a workspace slug and persist it to settings.
 */
async function switchWorkspace(): Promise<void> {
  const config = vscode.workspace.getConfiguration("flowamaz");
  const current = config.get<string>("workspace", "");
  const value = await vscode.window.showInputBox({
    prompt: "Flowamaz workspace slug or ID",
    value: current,
    placeHolder: "e.g. acme-prod",
  });
  if (value === undefined) {
    return;
  }
  await config.update(
    "workspace",
    value.trim(),
    vscode.ConfigurationTarget.Workspace
  );
  await vscode.commands.executeCommand("flowamaz.refreshWorkflows");
}
