# @flowamaz/cli

The `fmz` command-line interface for the [Flowamaz](https://flowamaz.io) workflow orchestration platform. Authenticate, manage workspaces, author and validate workflows, trigger and observe instances, and work with the connector/template Library — all from your terminal.

## Install

```bash
npm i -g @flowamaz/cli
fmz --help
```

Credentials are stored in your OS keychain via [`keytar`](https://www.npmjs.com/package/keytar) when available. `keytar` is an **optional** dependency with a native build; if it is unavailable, the CLI transparently falls back to `~/.fmz/credentials.json`. Install never fails on a missing `keytar` build.

## Authentication & environment

| Variable | Purpose |
|----------|---------|
| `FMZ_API_KEY` | A `fmz_`-prefixed API key. When set, it is sent as `Authorization: Bearer fmz_...` and overrides any stored user token. Use this in CI. |
| `FMZ_API_URL` | API base URL. Defaults to `https://api.flowamaz.io`. |
| `FMZ_WORKSPACE` | Default workspace slug for workspace-scoped commands (overrides the one saved by `fmz workspace use`). |
| `FMZ_CONFIG_DIR` | Override the config/credentials directory (default `~/.fmz`). Useful for tests and sandboxes. |

Interactive login uses an OAuth-style device-code flow:

```bash
fmz auth login     # opens a browser, polls until authorized
fmz auth whoami
fmz auth logout
```

## Global flags

`--json` and `--yaml` switch the output format; `--quiet` suppresses informational/success messages (errors still print).

## Commands

```
fmz auth        login | logout | whoami
fmz workspace   list | use <slug> | create <name> [--slug] | members <ws>
fmz workflow    list | get <id> | create <file> | update <id> <file>
                validate <file> | publish <id> [--environment]
                deploy <id> --environment <env> [--test]
fmz instance    trigger <workflowId> [--payload <json>] [--wait]
                status <id> | logs <id> [--follow] | cancel <id> | retry <id>
fmz library     list [--templates] | search <q> [--templates] | show <id>
                install <id> | outdated | update <id>
                connector init|validate|publish [file]
                template  init|validate|publish [file]
fmz version     history <workflowId> | diff <workflowId> <from> [to]
                checkout <workflowId> <sha> [--out <file>]
```

### Examples

```bash
# Select a workspace, then validate and deploy a workflow
fmz workspace use ops
fmz workflow validate ./order-flow.yaml
fmz workflow deploy 4f8c... --environment staging --test

# Trigger and wait
fmz instance trigger 4f8c... --payload '{"orderId":"123"}' --wait

# Author a connector
fmz library connector init my-connector
fmz library connector validate my-connector/connector.yaml
fmz library connector publish my-connector/connector.yaml
```

## Development

```bash
npm install
npm run build   # tsc, strict mode → dist/
npm test        # vitest
```

## License

MIT
