---
title: What is a Workspace?
updated: 2026-05-24
readingTime: "3 min read"
plans: "All plans"
---

# What is a Workspace?

A workspace is the isolation boundary in Flowamaz. Workflows, runs, credentials, and
members all live inside a workspace, and nothing leaks across the boundary. Workspace
isolation is enforced at every layer — there is no way for one workspace to read
another's data.

## Where workspaces sit in the hierarchy

```
Platform (the Flowamaz team)
  └── Organisation (your company)
        └── Workspace (a team or project)
              └── Environment (dev / staging / production)
```

Your organisation owns billing and SSO. Each workspace under it is self-contained, and
each workspace has its own dev, staging, and production environments.

## Workspace limits by plan

| Limit | Community | Starter | Pro | Enterprise |
|-------|-----------|---------|-----|-----------|
| Workspaces | 1 | 3 | 10 | Unlimited |
| Members / workspace | 1 | 10 | 50 | Unlimited |

Community is single-workspace, single-user. Cloud plans add more workspaces and team
members.

## Environments

Every workspace ships with three environments:

- **Dev** — where you build and experiment freely.
- **Staging** — where you validate before going live.
- **Production** — where real work runs.

Credentials are scoped per environment, so your test keys never reach production. See
[Environments](environments) for how promotion works.

## When to create multiple workspaces

- **One per team** — Finance, HR, and Engineering each get their own, so members,
  credentials, and workflows stay separate.
- **One per project** — a large initiative with its own integrations and access list.

If a small group shares everything, one workspace is enough. Split when isolation,
access control, or organisation actually calls for it.

To manage a workspace, open **Settings**. To add people, see
[Invite team members](invite-team-members).

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/workspaces/what-is-a-workspace.md)
