---
title: Replaying Instances
updated: 2026-05-30
readingTime: "3 min read"
plans: "All plans"
---

# Replaying Instances

**Replay** re-runs a finished instance from the start. It's the fastest way to retry a
failed run or reproduce a result, optionally with different input.

## When you can replay

You can replay any **terminal** instance — one that has finished in a `Succeeded`,
`Failed`, or `Cancelled` state. Running and waiting instances can't be replayed because
they haven't finished yet.

Common reasons to replay:

- A run **failed** on a transient error (a connector timed out) and you want to retry.
- You want to **reproduce** a past result to investigate behaviour.
- You're testing a fix by re-running the same scenario.

## Replay with the original payload

From an instance's detail page, click **Replay**. By default it starts a brand-new
instance using the same input payload the original ran with. The new instance gets its
own timeline and ID; the original is left untouched as a historical record.

## Replay with a payload override

You can also supply a JSON payload override when you replay. The override replaces the
original input for the new run, which lets you:

- Fix a bad value that caused the original to fail.
- Try the same workflow with a different scenario.

```json
{
  "amount": 4500,
  "requester": "alex"
}
```

The new instance runs the workflow's **published version** with your override as input.
Everything else — connectors, gates, AI nodes — behaves like a normal run.

## What replay does not do

Replay starts a fresh execution from the first node; it does not resume from the point
where the original stopped. If you need a partial re-run, branch your workflow logic to
skip already-completed work, or use a `TryCatch` to handle the failure inline.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/instances/replaying-instances.md)
