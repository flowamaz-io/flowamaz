---
title: Test Runs
updated: 2026-05-30
readingTime: "3 min read"
plans: "All plans"
---

# Test Runs

A **test run** lets you execute a workflow — including an unpublished draft — without
affecting production or your run limits. It's how you check your work before shipping.

## Test vs production

| | Test run | Production instance |
|---|----------|---------------------|
| Workflow version | Draft or published | Published only |
| Marked | `isTest: true` | Normal instance |
| Counts against run limits | No | Yes |
| Retention | Expires after 24h | Kept per plan retention |

A test run behaves exactly like a real one — connectors fire, AI nodes call models,
gates pause for approval — so what you see is what you'll get in production. Be aware
that side effects are real: a test run that sends a Slack message really sends it.

## Starting a test run

From the workflow editor, click **Run** and keep the **Test run** toggle on (it's on
by default while you're editing a draft). Supply an input payload and watch it
execute on the timeline like any other instance.

## The 24-hour expiry

Test instances and their data expire automatically 24 hours after they finish. This
keeps your Instances list focused on real runs. If you need to keep evidence of a test
result, capture it before the window closes.

## Run limits

Test runs **do not** count against your plan's run limit, so you can iterate freely
while building. Only production instances consume run quota. When you're confident the
workflow works, publish it and trigger production instances the normal way.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/instances/test-runs.md)
