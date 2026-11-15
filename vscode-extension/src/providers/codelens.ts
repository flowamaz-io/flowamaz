import * as vscode from "vscode";
import { computeCodeLenses } from "../core/codeLens";
import { isFlowamazWorkflow } from "../core/isFlowamazDoc";

/**
 * Code lens provider. Lens descriptors are computed by the pure
 * `core/codeLens` module; this wrapper anchors them to document ranges.
 */
export class FlowamazCodeLensProvider implements vscode.CodeLensProvider {
  public provideCodeLenses(
    document: vscode.TextDocument
  ): vscode.ProviderResult<vscode.CodeLens[]> {
    if (!isFlowamazWorkflow(document.getText(), document.fileName)) {
      return [];
    }
    return computeCodeLenses(document.getText()).map((info) => {
      const lineLength = document.lineAt(info.line).text.length;
      const range = new vscode.Range(info.line, 0, info.line, lineLength);
      return new vscode.CodeLens(range, {
        title: info.title,
        command: info.command,
        arguments: [document.uri],
      });
    });
  }
}
