/**
 * Code lens computation for Flowamaz workflow documents.
 * Pure module — no `vscode` import. Returns plain lens descriptors anchored
 * to a line index; the provider wrapper turns these into vscode.CodeLens.
 */

export interface LensInfo {
  /** Display text for the lens, e.g. `▶ Trigger`. */
  title: string;
  /** Command id the lens invokes when clicked. */
  command: string;
  /** Zero-based line index the lens is anchored to. */
  line: number;
}

/**
 * Compute the code lenses for a document. When the text is a Flowamaz workflow
 * (contains `kind: Workflow` or a top-level `workflow:` key) returns three
 * lenses anchored to that line; otherwise returns an empty array.
 */
export function computeCodeLenses(text: string): LensInfo[] {
  const lines = text.split(/\r?\n/);
  let anchor = -1;

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (line.includes("kind: Workflow")) {
      anchor = i;
      break;
    }
    // Top-level `workflow:` key (no leading indentation).
    if (/^workflow:\s*$/.test(line) || /^workflow:\s+\S/.test(line)) {
      anchor = i;
      break;
    }
  }

  if (anchor === -1) {
    return [];
  }

  return [
    { title: "▶ Trigger", command: "flowamaz.triggerWorkflow", line: anchor },
    { title: "📊 Open in App", command: "flowamaz.openInApp", line: anchor },
    { title: "✓ Validate", command: "flowamaz.validateWorkflow", line: anchor },
  ];
}
