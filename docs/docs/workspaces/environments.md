---
title: Environments
updated: 2026-05-24
readingTime: "3 min read"
plans: "Cloud only"
---

# Environments

Every workspace has three environments — **dev**, **staging**, and **production** — so
you can build and test safely before real work runs.

## What each environment is for

- **Dev** — your sandbox. Build, experiment, and break things freely.
- **Staging** — a production-like rehearsal. Validate behaviour with realistic config
  before going live.
- **Production** — where real workflows run against real systems and data.

## What changes between environments

The workflow *definition* is the same across environments; what differs is the
*configuration* around it. The most important difference is credentials:

- **Connector credentials are scoped per environment.** Your dev workflow can use a test
  Stripe key while production uses the live key — the workflow YAML references the same
  credential alias, and each environment resolves it to its own secret. Per-environment
  connector credentials arrive with connectors in Phase 4.

This means a workflow can never accidentally hit production systems with test data, or
vice versa.

## How a workflow is promoted

Flowamaz is Git-native, so promotion is a versioning operation, not a copy-paste:

1. **Dev** — you build on a branch and publish to dev to try it out.
2. **Staging** — when it's solid, you publish that version to staging for validation.
3. **Production** — once validated, you tag the version and promote it to production.

Each promotion pins the exact commit, so production always runs a known, reviewed
version. The full Git promotion workflow — branches, pull requests, and tags from the
canvas and the `fmz` CLI — lands in Phase 5; this is a preview of how it fits together.

For how environments sit inside the hierarchy, see
[What is a workspace](what-is-a-workspace).

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/workspaces/environments.md)
