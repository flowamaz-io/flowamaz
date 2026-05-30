---
title: What Are Human Gates?
updated: 2026-05-30
readingTime: "3 min read"
plans: "All plans"
---

# What Are Human Gates?

A **Human Gate** is a node that pauses a running workflow and waits for a person to
make a decision before it continues. It's how you put a human in the loop without
stopping automation everywhere else.

## How it works

When execution reaches a gate, the instance moves to a **Waiting** state and the
assignee is notified. The workflow stays paused — for minutes or days — until that
person responds. Their decision (and any input they provide) flows into the workflow
as variables, and execution resumes down the matching edge.

Because Flowamaz execution is durable, a waiting gate survives restarts and
deployments. The instance simply picks up where it left off when the decision arrives.

## What a gate captures

- **A decision** — typically approve or reject, but the options are configurable.
- **Optional input** — a form can collect structured data (a reason, an amount, a
  comment) alongside the decision.
- **Who and when** — the responder and timestamp are recorded for audit.

## Common use cases

- **Purchase approvals** — pause until a manager approves a PO over a threshold.
- **Content review** — hold a publish step until an editor signs off.
- **Onboarding checks** — wait for IT or HR to confirm a step is done.
- **Exception handling** — route edge cases to a person when the automated path
  isn't safe.

## Routing after a gate

The decision drives branching. Add a `condition` on the edges leaving the gate so an
**approved** decision takes one path and **rejected** takes another — for example,
create the PO on approval, or notify the requester on rejection.

To keep gates from blocking forever, configure an SLA timeout — see **Configuring
Gates**. To learn how people respond, see **Approving Gates**.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/gates/what-are-gates.md)
