---
prompt-id: 05-02-fmz-cli
phase: 05
sequence: 2
roles: [Executor, Verifier]
type: feature
depends-on: [05-01-git-workflow-versioning]
estimated-complexity: High
---

# fmz CLI — Complete Command Line Interface

## Context
Git versioning complete. Now build the fmz CLI — the developer-facing command line
tool that lets engineers work with Flowamaz workflows from their terminal.
See FUNCTIONAL.md §14 (CLI specification).

## Objective
Complete fmz CLI with all commands: auth, workspace, workflow CRUD, trigger,
status, logs, validate, deploy, library, and version commands.

## Scope

### What to Build

**CLI project structure:**

```
cli/
  src/
    commands/
      auth.ts         login, logout, whoami
      workspace.ts    list, use, create, members
      workflow.ts     list, get, create, validate, publish, deploy
      instance.ts     trigger, status, logs, cancel, retry
      library.ts      list, search, install, publish
      version.ts      history, diff, checkout
    lib/
      api-client.ts   authenticated Axios wrapper
      config.ts       ~/.fmz/config.json (token + workspace)
      output.ts       table, json, yaml formatters
      auth.ts         token storage (keychain on macOS, credential manager on Windows)
    index.ts          Commander.js entry point
  package.json
  tsconfig.json
  README.md
```

**Package:** `@flowamaz/cli` on npm, binary `fmz`

**Authentication commands:**
```bash
fmz login
# Opens browser to app.flowamaz.io/cli-auth
# Receives callback with token (device flow)
# Stores token in OS keychain

fmz logout
# Revokes token, removes from keychain

fmz whoami
# Shows: org, workspace, email, plan, token expiry
```

**Workspace commands:**
```bash
fmz workspace list
fmz workspace use <slug>
fmz workspace create --name "Finance Team"
fmz workspace members
```

**Workflow commands:**
```bash
fmz workflow list [--status draft|published]
fmz workflow get <id-or-slug>
fmz workflow validate <file.yaml>
# Uses Flowamaz.Validator (extracted NuGet) locally — zero API call
# Shows layer 1-6 issues with colours and line numbers

fmz workflow create --from <file.yaml>
fmz workflow update <id> --from <file.yaml>
fmz workflow publish <id> [--message "v1.2 — added CFO approval"]
fmz workflow deploy <id> --environment production
# deploy = publish + trigger a smoke-test instance (optional --test)
```

**Instance commands:**
```bash
fmz instance trigger <workflow-id-or-slug> [--payload '{"key":"value"}'] [--wait]
# --wait: polls until terminal state, shows progress

fmz instance status <instance-id>
# Shows: current status, current node, duration, pending gates

fmz instance logs <instance-id> [--follow]
# Streams WorkflowEvent log — one line per event

fmz instance cancel <instance-id>
fmz instance retry <instance-id>
```

**Library commands:**
```bash
fmz library list [--type connector|template] [--category finance]
fmz library search <query>
fmz library show <connector-id>
fmz library install <connector-id> [--workspace <slug>]
fmz library outdated
fmz library update <connector-id>

fmz connector init <name>   # scaffold connector folder
fmz connector validate <path>  # local validation
fmz connector publish <path>   # PR to flowamaz-io/connectors

fmz template init <name>
fmz template validate <path>
fmz template publish <path>
```

**Version commands:**
```bash
fmz version history <workflow-id>  # list commits
fmz version diff <workflow-id> <sha1>..<sha2>  # show diff
fmz version checkout <workflow-id> <sha>  # export YAML at commit
```

**Output formatting:**
- Default: human-readable tables (cli-table3)
- `--json`: JSON output (for scripting)
- `--yaml`: YAML output
- `--quiet`: suppress non-essential output (for CI)
- Colours: chalk — green for success, red for errors, amber for warnings

**Config file (~/.fmz/config.json):**
```json
{
  "api_url": "https://api.flowamaz.io",
  "current_workspace": "ws_finance",
  "current_org": "acme-corp",
  "token_expiry": "2026-06-25T14:00:00Z"
}
```

**CI/CD usage pattern:**
```bash
# GitHub Actions example (in workflow YAML):
- uses: actions/setup-node@v4
- run: npm install -g @flowamaz/cli
- run: fmz workflow validate ./workflows/purchase-approval.yaml
- run: fmz workflow deploy purchase-approval --environment production
  env:
    FMZ_API_KEY: ${{ secrets.FMZ_API_KEY }}
    FMZ_WORKSPACE: ${{ vars.FMZ_WORKSPACE }}
```

FMZ_API_KEY env var: uses workspace API key instead of user token (for CI).

**Backend — CLI auth endpoint:**
`POST /api/v1/cli/auth/device` — initiates device flow, returns device_code + polling URL
`GET /api/v1/cli/auth/token?device_code={code}` — poll for token (returns 202 pending or 200 with token)
`GET /api/v1/cli/auth/callback` — browser callback page that shows "CLI connected ✓"

**unit tests (CLI — Vitest):**
- api-client: attaches FMZ_API_KEY to request when env var set
- output: table formatter renders correct columns
- config: reads/writes ~/.fmz/config.json correctly
- workflow validate command: calls Flowamaz.Validator, outputs issues with correct colours

## Technical Requirements
- [ ] Commander.js for command parsing
- [ ] cli-table3 for table output
- [ ] chalk for colours
- [ ] Keytar for OS keychain token storage
- [ ] FMZ_API_KEY env var supported on all commands (CI mode)
- [ ] fmz workflow validate uses local Flowamaz.Validator (no API call)
- [ ] --json flag produces machine-readable output on all commands
- [ ] fmz instance logs --follow polls /events every 2 seconds, streams to terminal

## Acceptance Criteria
- [ ] fmz login → browser opens → token stored
- [ ] fmz workflow list → table of workflows
- [ ] fmz workflow validate workflow.yaml → Layer 1-6 issues shown with colours
- [ ] fmz instance trigger <id> --wait → polls and shows Completed/Failed
- [ ] FMZ_API_KEY env var → used instead of user token (verified by unit test)

## Output Expected
```
cli/src/ (complete CLI source)
cli/package.json
cli/tsconfig.json
cli/README.md
backend/Flowamaz.Api/Controllers/CliAuthController.cs
backend/Flowamaz.Tests.Unit/Cli/CliAuthControllerTests.cs
```
