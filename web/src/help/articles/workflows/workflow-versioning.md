---
title: Workflow Versioning
updated: 2026-05-30
readingTime: "3 min read"
plans: "All plans"
---

# Workflow Versioning

Flowamaz is Git-native. Every time you create, update, or publish a workflow, the
change is committed to a bare Git repository dedicated to your workspace. You get a
full history and diffs without touching the command line.

## History

Open a workflow and click **History** to see every commit in order — who made the
change, when, and whether it was a draft save or a publish. Each commit is a complete
snapshot of the `flowamaz/v1` definition at that point.

Because instances are pinned to the commit they ran on, you can always trace a past
run back to the exact workflow version that produced it.

## Diff view

Select any two commits in the history to see a side-by-side diff. The diff highlights
what changed in the YAML — added or removed nodes, re-routed edges, edited conditions,
and updated config. This makes review easy:

- Spot exactly what a teammate changed before approving.
- Confirm a fix is scoped to the node you intended.
- Understand why behaviour changed between two runs.

## Rollback

If a published version misbehaves, roll back from the history view:

1. Open **History** and find the known-good commit.
2. Click **Restore this version**.
3. The selected definition becomes your current draft.
4. **Publish** to pin it as the production version again.

Rollback never erases history — it adds a new commit that restores the older
definition, so the timeline stays complete and auditable.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/workflows/workflow-versioning.md)
