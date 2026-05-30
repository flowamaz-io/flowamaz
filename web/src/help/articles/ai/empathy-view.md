---
title: Empathy View
updated: 2026-05-30
readingTime: "3 min read"
plans: "All plans"
---

# Empathy View

**Empathy view** scores a workflow on how clear and humane it is to the people who run
it and interact with it, then lists concrete fixes. Think of it as a usability review
for your automation.

## What it scores

Empathy view inspects the workflow and rates it on clarity and UX, flagging issues
such as:

- **Unclear gate prompts** — a Human Gate whose title or description doesn't tell the
  approver what they're deciding or why.
- **Missing timeouts** — a gate with no SLA that could block forever.
- **Dead ends** — branches that don't lead to an `End` node, leaving runs stuck.
- **Silent failures** — error paths that don't notify anyone.
- **Confusing routing** — conditions that are hard to read or overlap.
- **No feedback** — a process that never tells the requester what happened.

You get an overall score plus the specific issues behind it.

## How to use it

1. Open a workflow and run **Empathy view**.
2. Read the score and the list of flagged issues, ordered by impact.
3. Click an issue to jump to the node or edge it refers to.
4. Apply the suggested fix — add a description, set a timeout, close a branch, add a
   notification.
5. Re-run empathy view to confirm the score improved.

## How to improve your score

- Give every gate a clear **title and description**.
- Set an **SLA timeout** on every production gate.
- Make sure **every branch reaches an End** node.
- Add a **notification** on rejection and failure paths so people aren't left guessing.
- Keep **conditions readable** and non-overlapping.

A high empathy score means the next person to run or approve this workflow will know
exactly what's happening and what to do — which is the whole point.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/ai/empathy-view.md)
