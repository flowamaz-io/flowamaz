# @flowamaz/cli

The `fmz` command-line interface for the [Flowamaz](https://flowamaz.io) workflow orchestration platform. Authenticate, manage workspaces, author and validate workflows, trigger and observe instances, and work with the connector/template Library — all from your terminal.

## Install

```bash
npm i -g @flowamaz/cli
fmz --help
```

Credentials are stored in your OS keychain via [`keytar`](https://www.npmjs.com/package/keytar) when available. `keytar` is an **optional** dependency with a native build; if it is unavailable, the CLI transparently falls back to `~/.fmz/credentials.json` (readable by you only). Install never fails on a missing `keytar` build.

## Authentication & environment

| Variable | Purpose |
|----------|---------|
| `FMZ_API_KEY` | A `fmz_`-prefixed API key. When set, it is sent as `Authorization: Bearer fmz_...` and overrides any stored user token. Use this in CI. |
| `FMZ_API_URL` | API base URL. Overrides an `api_url` saved in `~/.fmz/config.json`; defaults to `https://api.flowamaz.io`. |
| `FMZ_WORKSPACE` | Default workspace slug for workspace-scoped commands (overrides the one saved by `fmz workspace use`). |
| `FMZ_CONFIG_DIR` | Override the config/credentials directory (default `~/.fmz`). Useful for tests and sandboxes. |
| `FMZ_CREDENTIAL_STORE` | Set to `file` to keep credentials in `credentials.json` instead of the OS keychain (headless machines, tests). |
| `FMZ_CLIENT_NAME` | What this CLI is called in Settings → CLI sessions. Defaults to `fmz on <host name>`. |

Interactive login uses an OAuth-style device-code flow:

```bash
fmz auth login     # opens a browser, polls until authorized
fmz auth whoami
fmz auth logout    # ends this CLI session on the server, then forgets it locally
```

Approving the login gives this terminal a **CLI session** of its own (it is not a copy of your browser sign-in). The CLI keeps a long-lived refresh token and swaps it for a fresh 15-minute access token whenever it needs one, so it stays signed in while you use it. A session ends when you run `fmz auth logout`, when you end it in **Settings → CLI sessions** in the web app (the terminal is cut off straight away), when your password is reset or your account is deactivated, or after 30 days without use. Then run `fmz auth login` again.

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

# Library: browse, install, keep connectors up to date (add --template for templates, -w <ws> for another workspace)
fmz library search "invoice"
fmz library show acme/invoice-sync
fmz library install slack
fmz library install purchase-approval --template --name "Q4 purchase approval"
fmz library outdated              # installed connectors with a new major, or a deprecation notice
fmz library update acme/invoice-sync   # move to the latest major (workspace admin); minor/patch apply automatically
fmz library publisher acme          # a publisher's profile and everything they publish
fmz library submissions             # your organisation's submissions and where each stands (org owner)

# Author a connector (`fmz library connector …` works too; same for `template`)
fmz connector init my-connector
fmz connector validate my-connector/connector.yaml
fmz connector publish my-connector/connector.yaml
```

## Development

```bash
npm install
npm run build   # tsc, strict mode → dist/
npm test        # vitest
```

## License

MIT
