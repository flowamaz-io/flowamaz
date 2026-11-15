# Flowamaz for VS Code

Author, validate, and operate [Flowamaz](https://flowamaz.io) workflows without
leaving your editor. Provides hover docs, snippets, static graph diagnostics,
code lenses, and a workflow tree view backed by the Flowamaz API.

## Features

- **Hover docs** for workflow fields (`type`, `retry`, `timeout`, `condition`, ...)
  and every node type (`action`, `ai`, `human-gate`, `router`, `end`, `trigger`,
  `parallel`, `foreach`, `trycatch`).
- **Snippets** — type `fmz-` for `fmz-workflow`, `fmz-action`, `fmz-ai`,
  `fmz-gate`, `fmz-router`, `fmz-trycatch`, `fmz-parallel`, `fmz-foreach`.
- **Diagnostics** on open/save: local static graph analysis (orphaned nodes,
  edges to missing nodes, missing trigger / end) merged with authoritative
  results from `fmz workflow validate`.
- **Code lenses** on workflow documents: ▶ Trigger · 📊 Open in App · ✓ Validate.
- **Workflows view** in the activity bar, refreshed every 30s from the API.
- **JSON Schema** for workflow YAML (`*flowamaz-workflow.yaml`).

Features only activate for Flowamaz workflow documents — a file named
`*flowamaz-workflow.yaml`, or YAML containing `kind: Workflow`, or YAML with both
a top-level `workflow:` key and a `nodes:` section. Unrelated YAML is untouched.

## Install

**From VSIX**

```bash
npm install
npm run build
npx @vscode/vsce package
code --install-extension flowamaz-vscode-0.1.0.vsix
```

**From the Marketplace**

Search for "Flowamaz" in the Extensions view once published.

## Requirements

- The [`fmz` CLI](https://flowamaz.io/cli) must be on your `PATH` for the
  **Validate** and **Trigger** commands and for save-time validation. If it is
  missing, the extension degrades gracefully and still shows local graph
  diagnostics.
- For richest schema validation, enable
  [redhat.vscode-yaml](https://marketplace.visualstudio.com/items?itemName=redhat.vscode-yaml).
  This extension contributes the schema via `contributes.yamlValidation` for
  files matching `*flowamaz-workflow.yaml`. To associate the schema with other
  files, add to your settings:

  ```jsonc
  "yaml.schemas": {
    "./vscode-extension/src/schemas/workflow-v1.schema.json": "**/*.workflow.yaml"
  }
  ```

## Authentication

API-backed features (the Workflows tree view) use a Flowamaz API key, read from
the `FMZ_API_KEY` environment variable (the same key the `fmz` CLI uses). Keys
are prefixed `fmz_`. Launch VS Code from a shell where `FMZ_API_KEY` is exported,
or set it in your environment.

## Configuration

| Setting | Default | Description |
|---|---|---|
| `flowamaz.apiUrl` | `https://api.flowamaz.io` | Base URL of the Flowamaz API. |
| `flowamaz.workspace` | `""` | Default workspace slug or ID for API/CLI calls. |

## Commands

| Command | Title |
|---|---|
| `flowamaz.triggerWorkflow` | Flowamaz: Trigger Workflow |
| `flowamaz.validateWorkflow` | Flowamaz: Validate Workflow |
| `flowamaz.openInApp` | Flowamaz: Open in App |
| `flowamaz.showInstanceLogs` | Flowamaz: Show Instance Logs |
| `flowamaz.switchWorkspace` | Flowamaz: Switch Workspace |

## Development

```bash
npm install
npm run build   # tsc (strict) — 0 errors
npm test        # vitest run
```

The architecture keeps all logic in pure, `vscode`-free modules under
`src/core/` (unit-tested with vitest), with thin `vscode`-facing wrappers in
`src/providers/`, `src/views/`, and `src/commands/`. Tests never download a VS
Code binary.
