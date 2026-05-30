---
title: Approving Human Gates
updated: 2026-05-30
readingTime: "3 min read"
plans: "All plans"
---

# Approving Human Gates

When a workflow reaches a Human Gate, the assignee gets notified and can respond from
wherever they already work. There are three ways to act on a gate.

## In-app portal

Open Flowamaz and go to your gate inbox. Each pending gate shows its title,
description, the workflow it belongs to, and the available options. Pick an option
(and fill in the form if there is one), then submit. The workflow resumes immediately.

The portal is also where you see the full context — the instance timeline and
variables — if you need more information before deciding.

## Email links

When a gate is assigned, Flowamaz can send an email containing the task details and a
direct action link for each option. Click **Approve** or **Reject** right from the
email to record your decision without logging in first. The link is tied to that
specific gate, so it can't be reused for anything else.

## Slack / Teams

If your workspace connects Slack or Microsoft Teams, the gate posts an interactive
message with buttons. Click a button in the channel or DM to respond, and the workflow
continues. This keeps approvals where conversations already happen.

## What happens after you respond

- The instance leaves the **Waiting** state and continues.
- Your decision and any form input become workflow variables, so later edges can route
  on them.
- The responder and timestamp are recorded for the audit log.

## If no one responds

If the gate has an SLA timeout and the window passes before anyone acts, the gate's
configured timeout behaviour takes over — auto-approve, auto-reject, or escalate. Once
any one assignee responds, the gate is resolved and other notifications no longer
apply.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/gates/approving-gates.md)
