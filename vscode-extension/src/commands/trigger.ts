import * as vscode from "vscode";

/**
 * Trigger the active workflow by running the `fmz` CLI in an integrated
 * terminal. Requires the `fmz` CLI on PATH.
 */
export function triggerWorkflow(uriArg?: vscode.Uri): void {
  const uri = uriArg ?? vscode.window.activeTextEditor?.document.uri;
  if (!uri) {
    void vscode.window.showWarningMessage(
      "Flowamaz: open a workflow YAML file before triggering."
    );
    return;
  }
  const terminal = vscode.window.createTerminal("Flowamaz");
  terminal.show();
  terminal.sendText(
    "fmz workflow deploy " + quote(uri.fsPath) + " --environment dev --test"
  );
}

function quote(p: string): string {
  return '"' + p.replace(/"/g, '\\"') + '"';
}
