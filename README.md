# Flowamaz — open source

Open parts of [Flowamaz](https://flowamaz.com), the AI-native workflow platform.

| Directory | What it is | Licence |
|-----------|-----------|---------|
| `dotnet/Flowamaz.Engine.Core` | Engine core (NuGet): workflow YAML parser, JSON Schema and validator, condition evaluator, gate forms, cron schedules, Workflow Twin simulator. [README](dotnet/Flowamaz.Engine.Core/README.md) | MIT |
| `dotnet/Flowamaz.Connectors.Sdk` | Connector SDK (NuGet): the contract a connector operation implements, credentials, errors, manifests, egress guard. [README](dotnet/Flowamaz.Connectors.Sdk/README.md) | MIT |
| `dotnet/Flowamaz.Connectors.Official` | The 13 official connectors (NuGet): HTTP/REST, webhooks (receive and emit), Slack, Microsoft Teams, Microsoft 365, GitHub, email, PostgreSQL, MySQL, file system, script/shell, schedule (cron). [README](dotnet/Flowamaz.Connectors.Official/README.md) | MIT |
| `dotnet/Flowamaz.Engine.Runtime` | Workflow runtime (NuGet): the orchestrator (durable, event-sourced runs), saga compensation, retries with backoff, HTTP actions, approval gates (signed email links, approve/reject), variables and conditions. [README](dotnet/Flowamaz.Engine.Runtime/README.md) | MIT |
| `dotnet/Flowamaz.Engine.Persistence` | Runtime storage (NuGet): EF Core and PostgreSQL, with its own migrations; EF Core's in-memory provider works for a single process. [README](dotnet/Flowamaz.Engine.Persistence/README.md) | MIT |
| `dotnet/Flowamaz.Engine.Hosting` | Run it in a .NET host (NuGet): the background worker, the Redis or in-memory task queue and one-call service registration. [README](dotnet/Flowamaz.Engine.Hosting/README.md) | MIT |
| `canvas/` | `@flowamaz/canvas`: the workflow canvas for Vue 3 (YAML ↔ graph, Cytoscape.js composable, YAML patching, toolbar, palette, minimap). See [canvas/README.md](canvas/README.md) | MIT |
| `cli/` | `fmz`, the Flowamaz command-line interface (`npm i -g @flowamaz/cli`) | MIT |
| `vscode-extension/` | Author, validate and run Flowamaz workflows from VS Code | MIT |
| `docs/yaml-spec.md` | Workflow YAML specification v1 | MIT |
| `docs/connector-spec.md` | Connector `connector.yaml` specification | MIT |

Next: AI steps with your own model key (metered), then a self-hostable Community Edition.

## How this repository is maintained

This repository is published automatically from Flowamaz's development repository: every change there to the open parts is copied here as one commit ("Export from platform@…"). Pull requests here are welcome. When one is accepted, a maintainer applies it in the development repository, and it comes back here with the next export, credited to you. The pull request is then closed with a link to that export.

## Build and test

```bash
dotnet test dotnet/Flowamaz.slnx
npm ci --prefix canvas && npm test --prefix canvas && npm run typecheck --prefix canvas
npm ci --prefix cli && npm run build --prefix cli && npm test --prefix cli
npm ci --prefix vscode-extension && npm run build --prefix vscode-extension && npm test --prefix vscode-extension
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Security issues: see [SECURITY.md](SECURITY.md); please don't open a public issue.
