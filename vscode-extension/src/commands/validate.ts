import * as vscode from "vscode";

/**
 * Validate the active workflow by running `fmz workflow validate` in an
 * integrated terminal. Requires the `fmz` CLI on PATH.
 */
export function validateWorkflow(uriArg?: vscode.Uri): void {
  const uri = uriArg ?? vscode.window.activeTextEditor?.document.uri;
  if (!uri) {
    void vscode.window.showWarningMessage(
      "Flowamaz: open a workflow YAML file before validating."
    );
    return;
  }
  const terminal = vscode.window.createTerminal("Flowamaz");
  terminal.show();
  terminal.sendText("fmz workflow validate " + quote(uri.fsPath));
}

function quote(p: string): string {
  return '"' + p.replace(/"/g, '\\"') + '"';
}
