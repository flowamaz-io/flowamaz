---
title: Migrate from Community to Cloud
updated: 2026-05-24
readingTime: "3 min read"
plans: "Cloud only"
---

# Migrate from Community to Cloud

When your self-hosted Community workspace outgrows its limits, you can move it to
Flowamaz Cloud in a single command. Your workflows and history come with you.

## When to migrate

- **You've hit a limit** — 5 workflows, 500 runs a month, or the 7-day history window.
- **You need a team** — Community is single-user; Cloud adds roles and multi-member
  workspaces.
- **You need SSO** — single sign-on, SAML, and SCIM are Cloud features.
- **You want managed infrastructure** — no more running and patching your own stack.

## How to migrate

1. Make sure you're signed in to Cloud from the CLI:
   ```bash
   fmz auth login
   ```
2. Run the migration, naming the local workspace you want to move:
   ```bash
   fmz cloud migrate --workspace ws_my_team
   ```
3. The CLI uploads your workflow definitions, run history, and Git repository to your
   Cloud organisation and prints the new workspace URL when it's done.

## What carries over

- **Workflows** — every definition imports instantly, including YAML, canvas layout,
  and descriptions.
- **Run history** — past instances are preserved (subject to your Cloud plan's
  retention window).
- **Git repositories** — your full version history travels with the workspace; branches
  and tags are intact.

## What you reconfigure

- **Credentials** — for security, credential values are never exported. Re-enter each
  one in **Settings → Credentials** on Cloud. The aliases your workflows reference stay
  the same, so nothing in the definitions needs editing.

## How long it takes

Under 5 minutes for a typical workspace. Larger workspaces with long run histories take
a little longer while history uploads, but you can keep working during the transfer.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/getting-started/migrate-community-to-cloud.md)
