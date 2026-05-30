# Security

## Reporting vulnerabilities

If you believe you've found a security vulnerability in Flowamaz, please email
**security@flowamaz.io**. Do not open a public GitHub issue for security reports.

- Include a description, reproduction steps, and impact assessment.
- Encrypt sensitive details with our PGP key (published at https://flowamaz.io/.well-known/pgp-key.asc).
- We acknowledge reports within 2 business days and aim to provide a remediation timeline within 7 days.

Please act in good faith: do not access or modify other users' data, and give us reasonable time to
remediate before any disclosure.

## Security features

- **HMAC-signed webhooks** — inbound webhook endpoints verify an HMAC signature; deliveries are
  idempotent via idempotency keys. Stripe webhooks verify the `Stripe-Signature` (`t=`,`v1=`) header
  and reject invalid signatures with `400`.
- **Authentication** — custom JWT (not ASP.NET Identity); short-lived access tokens signed with
  `JWT_SECRET`. CLI/API access uses scoped workspace API keys that can be rotated and revoked.
- **SSO** — SAML 2.0 and OIDC with JIT provisioning for Enterprise organisations.
- **RBAC + workspace isolation** — every workspace-scoped query filters by `workspace_id`; cross-
  workspace access is impossible at every layer. Roles range from Viewer to Admin (plus platform and
  org roles).
- **Credential vault** — connector credentials are encrypted at rest with `CREDENTIAL_MASTER_KEY`.
  Credential values are never returned after creation, never logged, and never placed in webhook
  payloads — only opaque aliases are ever visible.
- **Audit log** — an append-only record of who did what, when. No update/delete API path exists;
  entries are removed only by the retention job. Exportable as CSV by org admins.
- **Rate limiting** — Redis-backed limits protect auth and AI endpoints (e.g. Co-pilot is capped per
  user per hour).
- **Signed human-gate links** — approval links delivered by email/Slack are HMAC-signed with
  `GATE_SIGNING_KEY` and expire.
- **Transport & headers** — the nginx reverse proxy terminates TLS and sets HSTS and standard
  security headers; CORS is an explicit origin allowlist.
- **Development-only surfaces** — the workflow breakpoint/step debugger endpoints return `404` outside
  the Development environment and additionally require workspace membership.

## Data handling

- **What is stored** — organisation/workspace metadata, users, workflow definitions and YAML,
  instance run history and event logs, connector credentials (encrypted), audit events, and AI token
  usage metering. Workflow node outputs are stored to power monitoring and replay.
- **Encryption** — credentials are encrypted at rest with AES via `CREDENTIAL_MASTER_KEY`; data in
  transit is protected by TLS. Sensitive workflow variables are stripped from debugger snapshots.
- **Retention** — audit events follow a configured retention window enforced by a background job;
  test-run instances expire automatically after 24 hours.
- **AI metering** — every AI call records tokens in/out, model id, function id, workspace id, and
  cost. No AI feature ships without metering.
- **Secrets** — never committed to the repository. All secrets are supplied via environment variables
  (see the env var reference in [DEVELOPER.md](./DEVELOPER.md)); non-Development startup fails fast if
  required secrets are missing.
