---
title: Publishing Workflows
updated: 2026-05-30
readingTime: "3 min read"
plans: "All plans"
---

# Publishing Workflows

A workflow is a **draft** while you build it and a **published** version once it's
ready to run in production. Publishing is the step that turns "I'm working on this"
into "this is live."

## Draft vs published

- **Draft** — every edit you make lives here. Drafts can still be triggered as test
  runs so you can check your work, but they are not the production definition.
- **Published** — a pinned, immutable version. Production triggers (webhooks, API,
  schedules) run the published version, not your in-progress draft.

This separation means you can keep editing safely without affecting what's running
in production.

## Validation runs first

When you click **Publish**, Flowamaz validates the workflow before anything is
pinned. Validation checks include:

- The YAML conforms to the `flowamaz/v1` schema.
- Every edge connects real nodes, and there are no orphans.
- There is a reachable terminal (`End`) node.
- Referenced connectors and credentials exist.

If validation fails, you get a list of specific issues to fix — publishing is blocked
until they're resolved.

## The production version

Publishing pins the current draft as the new production version and commits it to the
workspace Git history. From that moment:

- New production instances run the pinned version.
- Already-running instances finish on the version they started with.
- The pinned version is recorded in Git, so you always know exactly what's live.

To ship a change, edit the draft, test it, then publish again. Each publish creates a
new production version while the previous ones remain in history for rollback.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/workflows/publishing-workflows.md)
