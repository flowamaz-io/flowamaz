# Flowamaz — open source tools

Open parts of [Flowamaz](https://flowamaz.com), the AI-native workflow platform.

| Directory | What it is | Licence |
|-----------|-----------|---------|
| `engine/` | `Flowamaz.Engine.Core` (NuGet): workflow YAML parser, JSON Schema and validator, condition evaluator, gate forms, Workflow Twin simulator | MIT |
| `cli/` | `fmz`, the Flowamaz command-line interface (`npm i -g @flowamaz/cli`) | MIT |
| `vscode-extension/` | Author, validate and run Flowamaz workflows from VS Code | MIT |
| `docs/yaml-spec.md` | Workflow YAML specification v1 | MIT |
| `docs/connector-spec.md` | Connector `connector.yaml` specification | MIT |

More will follow as parts of the platform are separated for open release: the canvas renderer, the connector SDK and official connectors, the engine runtime, and a self-hostable Community Edition.

## Build and test

```bash
dotnet test engine/Flowamaz.Engine.slnx
npm ci --prefix cli && npm run build --prefix cli && npm test --prefix cli
npm ci --prefix vscode-extension && npm run build --prefix vscode-extension && npm test --prefix vscode-extension
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Security issues: see [SECURITY.md](SECURITY.md); please don't open a public issue.
