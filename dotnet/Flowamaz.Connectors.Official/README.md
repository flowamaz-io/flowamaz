# Flowamaz.Connectors.Official

The official Flowamaz connectors (MIT), built on `Flowamaz.Connectors.Sdk`:

| Connector | Operations |
|---|---|
| `http-rest` | GET, POST, PUT, PATCH, DELETE to any public HTTPS API, with Bearer, API-key or Basic credentials |
| `webhook-receive` | Starts workflows from HMAC-signed webhooks (trigger) |
| `webhook-emit` | `send-webhook`: HMAC-signed outgoing webhooks |
| `schedule-cron` | `validate-cron`: checks a cron expression and previews the next runs |
| `email-smtp` | `send-email` through the host's email service, with per-workspace limits |
| `slack` | `send-message`, `send-dm`, `post-approval-message` |
| `microsoft-teams` | `send-message`, `send-webhook-message`, `post-adaptive-card` |
| `microsoft-365` | `send-mail`, `create-calendar-event`, `upload-file` |
| `github` | `create-issue`, `get-issue`, `create-pr`, `add-comment` |
| `postgresql`, `mysql` | `query`, `execute`: parameterised SQL; connection strings only from credentials |
| `file-system` | `read-file`, `write-file`, `list-files`, jailed to a per-workspace folder (off unless a root path is configured) |
| `script-shell` | `run-script`: bash or Python with arguments on stdin (off unless enabled) |

`OfficialConnectorCatalog` is the single source of truth for every operation: inputs, whether a credential is needed, allowed domains and whether it runs sandboxed. A test checks that every catalogue operation has a handler and every handler is in the catalogue.

Every outbound call goes through the SDK's egress guard. Secrets in inputs are rejected.

Moved code keeps its original namespaces (`Flowamaz.Application.Connectors.*`).
