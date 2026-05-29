import * as vscode from "vscode";
import { getSnippets } from "../core/snippets";
import { isFlowamazWorkflow } from "../core/isFlowamazDoc";

/**
 * Completion provider that surfaces Flowamaz snippets. Snippet defs come from
 * the pure `core/snippets` module.
 */
export class FlowamazCompletionProvider implements vscode.CompletionItemProvider {
  public provideCompletionItems(
    document: vscode.TextDocument
  ): vscode.ProviderResult<vscode.CompletionItem[]> {
    if (!isFlowamazWorkflow(document.getText(), document.fileName)) {
      return undefined;
    }
    return getSnippets().map((snippet) => {
      const item = new vscode.CompletionItem(
        snippet.label,
        vscode.CompletionItemKind.Snippet
      );
      item.insertText = new vscode.SnippetString(snippet.body.join("\n"));
      item.detail = snippet.description;
      item.documentation = new vscode.MarkdownString(
        "```yaml\n" + snippet.body.join("\n") + "\n```"
      );
      return item;
    });
  }
}
