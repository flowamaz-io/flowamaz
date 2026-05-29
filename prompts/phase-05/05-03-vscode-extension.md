---
prompt-id: 05-03-vscode-extension
phase: 05
sequence: 3
roles: [Executor, Verifier, UX]
type: feature
depends-on: [05-02-fmz-cli]
estimated-complexity: Medium
---

# VS Code Extension — Workflow YAML Authoring Support

## Context
CLI complete. Now build the VS Code extension that makes workflow YAML editing
a first-class experience for developers. See FUNCTIONAL.md §15 (VS Code extension).

## Objective
VS Code extension with YAML syntax highlighting, schema validation, hover docs,
snippet library, and inline instance monitoring.

## Scope

### What to Build

**Extension project (vscode-extension/):**

```
vscode-extension/
  src/
    extension.ts        activation, command registration
    providers/
      hover.ts          field hover documentation
      completion.ts     snippet completions
      diagnostics.ts    validation squiggles (calls fmz validate)
      codelens.ts       "▶ Trigger" / "📊 Status" above workflow header
    views/
      instanceTree.ts   TreeDataProvider for Flowamaz sidebar
    schemas/
      workflow-v1.schema.json  (copy from backend)
    commands/
      trigger.ts        trigger workflow from editor
      validate.ts       run fmz validate on current file
      openInApp.ts      open workflow in browser
  package.json
  README.md
```

**Features:**

1. **YAML schema validation** (via JSON Schema association):
   - Associate `flowamaz-workflow.yaml` and files containing `kind: Workflow` with
     workflow-v1.schema.json
   - Built-in VS Code YAML Language Server picks this up automatically
   - Red squiggles on schema violations

2. **Custom diagnostics** (beyond JSON Schema):
   - On file save: run `fmz workflow validate` (subprocess)
   - Parse output, push to VS Code DiagnosticCollection
   - Shows Layer 2-6 issues (graph, expression, security, best-practice) as squiggles

3. **Hover documentation:**
   - Hover any field name → show description from yaml-spec.md
   - Hover node `type:` value → show that node type's documentation
   - Hover variable `{{ expression }}` → show variable type and where it comes from

4. **Snippet completions (Ctrl+Space):**
   - `fmz-action`: full Action node template
   - `fmz-ai`: AI node template
   - `fmz-gate`: Human Gate template
   - `fmz-router`: Router with 2 outputs
   - `fmz-trycatch`: Try-Catch block
   - `fmz-parallel`: Parallel block
   - `fmz-foreach`: ForEach block
   - `fmz-workflow`: Complete minimal workflow scaffold

5. **CodeLens** (above `kind: Workflow` line):
   - `▶ Trigger` → runs `fmz instance trigger <workflowId>`, shows output in terminal
   - `📊 Open in App` → opens app.flowamaz.io/workflows/{id} in browser
   - `✓ Validate` → runs validation, shows results in Problems panel

6. **Flowamaz sidebar panel:**
   - Shows all workflows in current workspace
   - Each workflow: name, status badge (Draft/Published), last modified
   - Expand workflow → list of recent instances with status
   - Click instance → opens instance URL in browser
   - Refresh button (polls API every 30s)
   - Requires FMZ_API_KEY or fmz login credentials

7. **Status bar:**
   - Shows current workspace name (bottom right)
   - Click → workspace selector quick-pick
   - Green dot if connected, red dot if no credentials

8. **Commands (Command Palette):**
   - `Flowamaz: Trigger Workflow`
   - `Flowamaz: Validate Workflow`
   - `Flowamaz: Open in App`
   - `Flowamaz: Show Instance Logs`
   - `Flowamaz: Switch Workspace`

**unit tests (Mocha/VS Code extension test framework):**
- Hover provider returns documentation for `type:` field
- Completion provider returns `fmz-action` snippet
- DiagnosticsProvider: orphaned node → diagnostic added
- CodeLens provider: workflow file → 3 code lenses above kind: Workflow

## Technical Requirements
- [ ] Uses VS Code Extension API (vscode npm package)
- [ ] Schema validation via built-in YAML Language Server (no custom server)
- [ ] Custom diagnostics: subprocess call to fmz CLI (not direct API)
- [ ] Hover docs loaded from bundled yaml-spec.md at extension activation
- [ ] Sidebar: works with FMZ_API_KEY env var or fmz login credentials
- [ ] Does not activate for non-Flowamaz YAML files

## Acceptance Criteria
- [ ] Open a workflow YAML → red squiggles on schema errors
- [ ] Hover `type: action` → documentation shown in hover popup
- [ ] Ctrl+Space → `fmz-action` snippet available
- [ ] CodeLens `▶ Trigger` appears above `kind: Workflow`
- [ ] Sidebar panel shows workspace workflows

## Output Expected
```
vscode-extension/src/ (complete extension source)
vscode-extension/package.json (contributes: commands, languages, snippets)
vscode-extension/README.md
```
