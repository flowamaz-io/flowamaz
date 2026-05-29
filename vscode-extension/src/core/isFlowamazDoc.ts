/**
 * Activation gate: decide whether a document is a Flowamaz workflow.
 * Pure module — no `vscode` import. The extension must NOT light up features
 * for unrelated YAML.
 */

/**
 * Return true when the document looks like a Flowamaz workflow:
 *  - the file name matches `*flowamaz-workflow.yaml`, OR
 *  - the text contains `kind: Workflow`, OR
 *  - the text has both a top-level `workflow:` key and a `nodes:` section.
 */
export function isFlowamazWorkflow(text: string, fileName: string): boolean {
  if (/flowamaz-workflow\.yaml$/i.test(fileName)) {
    return true;
  }
  if (text.includes("kind: Workflow")) {
    return true;
  }
  const lines = text.split(/\r?\n/);
  const hasWorkflowKey = lines.some(
    (l) => /^workflow:\s*$/.test(l) || /^workflow:\s+\S/.test(l)
  );
  const hasNodesKey = lines.some((l) => /^\s*nodes:/.test(l));
  return hasWorkflowKey && hasNodesKey;
}
