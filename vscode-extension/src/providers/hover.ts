import * as vscode from "vscode";
import { getHoverDoc } from "../core/hoverDocs";
import { isFlowamazWorkflow } from "../core/isFlowamazDoc";

/**
 * Hover provider. The logic lives in `core/hoverDocs`; this wrapper only adapts
 * the VS Code API surface to that pure function.
 */
export class FlowamazHoverProvider implements vscode.HoverProvider {
  public provideHover(
    document: vscode.TextDocument,
    position: vscode.Position
  ): vscode.ProviderResult<vscode.Hover> {
    if (!isFlowamazWorkflow(document.getText(), document.fileName)) {
      return undefined;
    }
    const range = document.getWordRangeAtPosition(position, /[A-Za-z-]+/);
    if (!range) {
      return undefined;
    }
    const word = document.getText(range);
    const doc = getHoverDoc(word);
    if (!doc) {
      return undefined;
    }
    return new vscode.Hover(new vscode.MarkdownString(doc), range);
  }
}
