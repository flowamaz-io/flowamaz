---
prompt-id: 08-03-documentation
phase: 08
sequence: 3
roles: [Executor, Verifier]
type: feature
depends-on: [08-02-marketing-site]
estimated-complexity: Medium
---

# Documentation — Help Articles, API Docs, Developer Guide

## Context
The in-app help panel has article stubs from Phase 1. The API docs exist at /api-docs
but lack descriptions. Developer documentation is sparse. This prompt completes
all documentation before the comprehensive testing session.

## Objective
Complete in-app help articles, API endpoint descriptions, and developer guide.

## Scope

### What to Build

**In-app help articles (web/src/help/articles/):**

Complete articles for every major feature. Each article: 200-500 words,
markdown format, practical examples, no jargon.

Getting started:
- what-is-flowamaz.md ← update with Phase 6-7 features
- your-first-workflow.md ← step by step NL creation
- understanding-the-canvas.md ← nodes, edges, toolbar, copilot
- inviting-your-team.md ← members, roles, permissions

Workflows:
- creation-methods.md ← all 6 methods explained
- yaml-format.md ← flowamaz/v1 schema reference
- publishing-workflows.md ← draft vs published, validation
- workflow-versioning.md ← git history, diff view

Instances:
- triggering-workflows.md ← manual, webhook, API, schedule
- monitoring-instances.md ← timeline, variable inspector
- test-runs.md ← test vs production, 24h expiry
- replaying-instances.md ← replay with payload override

Human gates:
- what-are-gates.md ← concept, use cases
- configuring-gates.md ← assignee, options, SLA timeout
- approving-gates.md ← portal, email links, Slack/Teams

Connectors:
- installing-connectors.md ← library, credentials, OAuth
- http-rest-connector.md ← configuration reference
- connector-health.md ← health dashboard, troubleshooting

AI features:
- copilot.md ← commands, patch format, tips
- nl-workflow-creation.md ← form fields, tips for good results
- empathy-view.md ← score, issues, how to improve

Settings:
- api-keys.md ← creating, rotating, CI usage
- webhooks.md ← creating endpoints, HMAC, idempotency
- sso.md ← SAML setup, OIDC setup, JIT provisioning
- billing.md ← plans, trial, upgrading, invoices
- audit-log.md ← what is logged, export, retention

**Update HelpPanel article routing:**
In web/src/components/layout/HelpPanel.vue:
Update the articleMap to route each app page to the most relevant article.
Current routes that may be missing: /settings/webhooks, /settings/sso,
/settings/audit, /settings/billing, /library/templates/{id}.

**API endpoint descriptions (OpenAPI):**
Add XML doc comments to every public-facing controller action that lacks one.
Scalar picks these up automatically.

Focus on Phase 5-7 additions (Phase 1-4 likely already documented):
- WebhooksController: all 5 actions
- WebhookReceiveController: the receive action
- PublicWorkflowsController: all actions
- PublicInstancesController: all actions
- PublicGatesController: all actions
- BillingController: all actions
- AuditController: all actions
- TemplatesController: all actions
- SsoController: all actions
- NotificationsController: all actions

**docs/DEVELOPER.md — Developer guide:**

```markdown
# Flowamaz Developer Guide

## Architecture
[Clean architecture diagram description — Api, Application, Core, Infrastructure]

## Running locally
[Prerequisites, .env setup, docker compose up, verify health]

## Running tests
[Unit, integration, E2E commands]

## Making changes
[Branch naming, commit convention, PR process]

## Adding a connector
[Step by step: manifest, handler, tests, seed]

## Adding a workflow node type
[Step by step: enum, parser, executor, canvas icon]

## Environment variables reference
[Complete table of all env vars with description and example]

## Troubleshooting
[Common issues from testing: CORS, cookie, docker disk space, port conflicts]
```

**docs/SECURITY.md:**
```markdown
# Security

## Reporting vulnerabilities
[security@flowamaz.io, PGP key link]

## Security features
[HMAC webhooks, SSO, audit log, credential vault, rate limiting]

## Data handling
[What data is stored, retention, encryption at rest]
```

After all documentation:
dotnet build — 0 errors (XML doc comments added)
npm run build --prefix web — 0 errors
git add . && git commit -m "docs: complete help articles, API descriptions, developer guide, security policy" && git push origin develop
