# Flowamaz — open source

Open parts of [Flowamaz](https://flowamaz.com), the AI-native workflow platform.

| Directory | What it is | Licence |
|-----------|-----------|---------|
| `engine/` | `Flowamaz.Engine.Core` (NuGet): workflow YAML parser, JSON Schema and validator, condition evaluator, gate forms, Workflow Twin simulator. See [engine/Flowamaz.Engine.Core/README.md](engine/Flowamaz.Engine.Core/README.md) | MIT |
| `cli/` | `fmz`, the Flowamaz command-line interface (`npm i -g @flowamaz/cli`) | MIT |
| `vscode-extension/` | Author, validate and run Flowamaz workflows from VS Code | MIT |
| `docs/yaml-spec.md` | Workflow YAML specification v1 | MIT |
| `docs/connector-spec.md` | Connector `connector.yaml` specification | MIT |

More will follow as parts of the platform are separated for open release: the canvas renderer, the connector SDK and official connectors, the engine runtime, and a self-hostable Community Edition.

## How this repository is maintained

This repository is published automatically from Flowamaz's development repository: every change there to the open parts is copied here as one commit ("Export from platform@…"). Pull requests here are welcome. When one is accepted, a maintainer applies it in the development repository, and it comes back here with the next export, credited to you. The pull request is then closed with a link to that export.

## Build and test

```bash
dotnet test engine/Flowamaz.Engine.slnx
npm ci --prefix cli && npm run build --prefix cli && npm test --prefix cli
npm ci --prefix vscode-extension && npm run build --prefix vscode-extension && npm test --prefix vscode-extension
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Security issues: see [SECURITY.md](SECURITY.md); please don't open a public issue.
