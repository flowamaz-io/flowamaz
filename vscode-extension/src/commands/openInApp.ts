import * as vscode from "vscode";

/**
 * Open the Flowamaz web app (workspace dashboard) in the default browser.
 * Derives the web URL from the configured API URL by dropping the `api.`
 * subdomain when present.
 */
export async function openInApp(): Promise<void> {
  const config = vscode.workspace.getConfiguration("flowamaz");
  const apiUrl = config.get<string>("apiUrl", "https://api.flowamaz.io");
  const workspace = config.get<string>("workspace", "").trim();
  const appBase = apiUrl.replace("://api.", "://app.");
  const target =
    workspace.length > 0
      ? appBase + "/workspaces/" + encodeURIComponent(workspace)
      : appBase;
  await vscode.env.openExternal(vscode.Uri.parse(target));
}
