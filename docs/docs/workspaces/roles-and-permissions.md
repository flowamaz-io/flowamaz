---
title: Roles and Permissions
updated: 2026-05-24
readingTime: "5 min read"
plans: "Cloud only"
---

# Roles and Permissions

Every workspace member has exactly one role. Roles are ordered by power, and each one
grants a fixed set of permissions. This page explains all five in plain English.

## The five workspace roles

- **Admin** (level 5) — full control of the workspace. Publishes to production, manages
  credentials, members, and settings, and decides any human gate.
- **Designer** (level 4) — builds workflows. Creates and edits definitions, publishes to
  dev and staging (but not production), uses connectors, and triggers and manages runs.
- **Operator** (level 3) — runs the show day to day. Triggers, cancels, and retries runs,
  views all run details and variables, and reads the audit log — but can't edit
  workflow definitions.
- **Runner** (level 2) — limited operator. Triggers only the workflows assigned to them
  and sees only their own runs.
- **Viewer** (level 1) — read-only. Sees workflow definitions and run history, but never
  variable values.

## Permission map

| Permission | Admin | Designer | Operator | Runner | Viewer |
|------------|:-----:|:--------:|:--------:|:------:|:------:|
| workflows.read | ✓ | ✓ | ✓ | — | ✓ |
| workflows.create / update | ✓ | ✓ | — | — | — |
| workflows.publish.dev / staging | ✓ | ✓ | — | — | — |
| workflows.publish.production | ✓ | — | — | — | — |
| instances.trigger | ✓ | ✓ | ✓ | assigned only | — |
| instances.read | ✓ | ✓ | ✓ | own only | ✓ |
| instances.cancel / retry | ✓ | ✓ | ✓ | — | — |
| instances.variables.read | ✓ | ✓ | ✓ | — | — |
| credentials.manage | ✓ | — | — | — | — |
| connectors.configure | ✓ | ✓ | — | — | — |
| members.manage | ✓ | — | — | — | — |
| workspace.settings | ✓ | — | — | — | — |
| audit.read | ✓ | — | ✓ | — | — |
| gates.decide | ✓ | assigned | assigned | — | — |

Designers and Operators can decide only the gates assigned to them; Admins can decide
any gate.

## Who can do what — common scenarios

| Question | Answer |
|----------|--------|
| Can a Designer publish to production? | No — production publish requires Admin. |
| Can an Operator edit a workflow definition? | No — Operators run workflows but can't change them. |
| Can a Viewer see variable values? | No — Viewers see definitions and history only. |
| Can a Runner trigger any workflow? | No — only the workflows assigned to them. |
| Can a Designer manage credentials? | No — credential management is Admin-only. |
| Can an Operator retry a failed run? | Yes — Operators can cancel and retry runs. |
| Can an Admin invite new members? | Yes — member management is an Admin permission. |

## Changing a role

Admins change roles in **Settings → Members**. See
[Invite team members](invite-team-members). Pick the lowest role that lets someone do
their job — it's the safest default.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/workspaces/roles-and-permissions.md)
