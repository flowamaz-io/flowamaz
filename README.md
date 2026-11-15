# Flowamaz — open source tools

Open parts of [Flowamaz](https://flowamaz.com), the AI-native workflow platform.

| Directory | What it is | Licence |
|-----------|-----------|---------|
| `cli/` | `fmz`, the Flowamaz command-line interface (`npm i -g @flowamaz/cli`) | MIT |
| `vscode-extension/` | Author, validate and run Flowamaz workflows from VS Code | MIT |
| `docs/yaml-spec.md` | Workflow YAML specification v1 | MIT |
| `docs/connector-spec.md` | Connector `connector.yaml` specification | MIT |

More will follow as parts of the platform are separated for open release: spec JSON Schemas, connector and component SDKs, and the App Twin runner.

## Build and test

```bash
npm ci --prefix cli && npm run build --prefix cli && npm test --prefix cli
npm ci --prefix vscode-extension && npm run build --prefix vscode-extension && npm test --prefix vscode-extension
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Security issues: see [SECURITY.md](SECURITY.md); please don't open a public issue.
