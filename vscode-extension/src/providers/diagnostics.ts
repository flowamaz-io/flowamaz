import * as vscode from "vscode";
import { execFile } from "child_process";
import { findGraphDiagnostics, Diagnostic as CoreDiagnostic } from "../core/graphDiagnostics";
import { isFlowamazWorkflow } from "../core/isFlowamazDoc";

/**
 * Shape of a single issue parsed out of `fmz workflow validate --json`.
 * The CLI / API returns `{ errors: [...], warnings: [...] }`.
 */
interface CliValidationIssue {
  message?: string;
  line?: number;
}

interface CliValidationResponse {
  errors?: CliValidationIssue[];
  warnings?: CliValidationIssue[];
}

function toVsSeverity(s: CoreDiagnostic["severity"]): vscode.DiagnosticSeverity {
  return s === "error"
    ? vscode.DiagnosticSeverity.Error
    : vscode.DiagnosticSeverity.Warning;
}

function makeDiagnostic(
  message: string,
  line: number,
  severity: vscode.DiagnosticSeverity
): vscode.Diagnostic {
  const safeLine = Number.isFinite(line) && line >= 0 ? line : 0;
  const range = new vscode.Range(safeLine, 0, safeLine, Number.MAX_SAFE_INTEGER);
  const diag = new vscode.Diagnostic(range, message, severity);
  diag.source = "flowamaz";
  return diag;
}

/**
 * Run the `fmz` CLI validator out-of-process. Resolves to the parsed response
 * or `undefined` when the CLI is missing / errors — failures degrade
 * gracefully so local graph diagnostics still surface.
 */
function runCliValidate(filePath: string): Promise<CliValidationResponse | undefined> {
  return new Promise((resolve) => {
    execFile(
      "fmz",
      ["workflow", "validate", filePath, "--json"],
      { timeout: 15000 },
      (_err, stdout) => {
        if (!stdout) {
          resolve(undefined);
          return;
        }
        try {
          const parsed = JSON.parse(stdout) as CliValidationResponse;
          resolve(parsed);
        } catch {
          resolve(undefined);
        }
      }
    );
  });
}

/**
 * Diagnostics manager. On document save it merges:
 *  1. local static graph diagnostics (pure `core/graphDiagnostics`), and
 *  2. authoritative results from the `fmz` CLI (best-effort).
 */
export class FlowamazDiagnostics {
  private readonly collection: vscode.DiagnosticCollection;

  constructor() {
    this.collection = vscode.languages.createDiagnosticCollection("flowamaz");
  }

  public register(context: vscode.ExtensionContext): void {
    context.subscriptions.push(this.collection);
    context.subscriptions.push(
      vscode.workspace.onDidSaveTextDocument((doc) => {
        void this.refresh(doc);
      })
    );
    context.subscriptions.push(
      vscode.workspace.onDidOpenTextDocument((doc) => {
        void this.refresh(doc);
      })
    );
    if (vscode.window.activeTextEditor) {
      void this.refresh(vscode.window.activeTextEditor.document);
    }
  }

  public async refresh(document: vscode.TextDocument): Promise<void> {
    const text = document.getText();
    if (!isFlowamazWorkflow(text, document.fileName)) {
      this.collection.delete(document.uri);
      return;
    }

    const diagnostics: vscode.Diagnostic[] = findGraphDiagnostics(text).map((d) =>
      makeDiagnostic(d.message, d.line, toVsSeverity(d.severity))
    );

    const cli = await runCliValidate(document.uri.fsPath);
    if (cli) {
      for (const e of cli.errors ?? []) {
        diagnostics.push(
          makeDiagnostic(
            e.message ?? "Validation error.",
            (e.line ?? 1) - 1,
            vscode.DiagnosticSeverity.Error
          )
        );
      }
      for (const w of cli.warnings ?? []) {
        diagnostics.push(
          makeDiagnostic(
            w.message ?? "Validation warning.",
            (w.line ?? 1) - 1,
            vscode.DiagnosticSeverity.Warning
          )
        );
      }
    }

    this.collection.set(document.uri, diagnostics);
  }
}
