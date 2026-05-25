---
title: API Keys
updated: 2026-05-24
readingTime: "4 min read"
plans: "Cloud only"
---

# API Keys

API keys let external systems talk to a workspace without a human logging in — for
CI/CD pipelines, scripts, and other applications that trigger or query workflows.

## What API keys are for

- **CI/CD** — deploy or trigger workflows from a pipeline.
- **External systems** — let another application start runs or read instance status.
- **Automation** — scripts that pull run history or manage webhooks.

## Create a key

1. Open **Settings → API Keys**.
2. Click **Create**.
3. Give it a clear name (e.g. "GitHub Actions — deploy").
4. Choose the **scopes** it needs (see below).
5. Copy the key when it's shown. **This is the only time the full value appears** — store
   it somewhere safe immediately.

## Scopes

Grant only the scopes a key actually needs:

| Scope | Allows |
|-------|--------|
| `workflows:read` | List and read workflow definitions |
| `workflows:trigger` | Start a workflow run |
| `instances:read` | Read run status and history |
| `instances:write` | Cancel, retry, or update runs |
| `instances:variables:read` | Read variable values from runs |
| `webhooks:manage` | Create and manage webhook endpoints |

*Example:* a status dashboard only needs `instances:read`. A deploy pipeline that kicks
off a run needs `workflows:trigger`.

## Key format

Keys carry their environment in the prefix so you can tell test from live at a glance:

```
Production:    fmz_live_{workspace_slug}_{env}_{random_32_chars}
Dev / Staging: fmz_test_{workspace_slug}_{env}_{random_32_chars}
```

- `fmz_live_…` keys act on production.
- `fmz_test_…` keys act on dev or staging only.

Always use a `fmz_test_` key in CI until you're ready for real runs.

## Security best practices

- **Never commit a key** to source control. Use your CI provider's secret store or an
  environment variable.
- **Scope tightly** — fewer permissions means less damage if a key leaks.
- **Rotate regularly** — create a new key, switch your systems over, then revoke the old
  one.
- **One key per system** so you can revoke a single integration without breaking others.

## Revoke a key

In **Settings → API Keys**, find the key and click **Revoke**. It stops working
instantly, and any system using it will get an authentication error. Managing API keys
requires the Admin role.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/workspaces/api-keys.md)
