---
title: Monitoring Instances
updated: 2026-05-30
readingTime: "3 min read"
plans: "All plans"
---

# Monitoring Instances

When a workflow runs, its instance gives you a live view of every step. Open any
instance from the **Instances** list to see its timeline, variables, and status.

## The timeline

The timeline shows each node in execution order. As the instance runs, nodes light up
in real time:

- **Running** — currently executing.
- **Succeeded** — completed without error.
- **Failed** — threw an error; the timeline shows the message and which node.
- **Waiting** — paused at a Human Gate, Wait, or for an external event.

Click any node to see its inputs, outputs, duration, and any error detail. This makes
it easy to find exactly where a run went wrong.

## Variable inspector

The variable inspector shows the instance's data as it evolves. For each completed
node you can see the variables it read and the values it produced, so you can confirm
that data flowed through your conditions and connectors as expected.

Credential values are never shown here — only aliases appear, the same as everywhere
else in Flowamaz.

## Instance statuses

| Status | Meaning |
|--------|---------|
| **Running** | Actively executing. |
| **Waiting** | Paused at a gate, wait, or event. |
| **Succeeded** | Reached an `End` node cleanly. |
| **Failed** | Stopped on an unhandled error. |
| **Cancelled** | Stopped manually before finishing. |

`Succeeded`, `Failed`, and `Cancelled` are **terminal** — the instance won't change
again, though you can replay it. `Running` and `Waiting` are still in progress.

## Filtering

Use the status and date filters on the Instances list to find runs quickly — for
example, all `Failed` instances from the last 24 hours when investigating an issue.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/instances/monitoring-instances.md)
