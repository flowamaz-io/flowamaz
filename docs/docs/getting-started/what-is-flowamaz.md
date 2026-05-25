---
title: What is Flowamaz?
updated: 2026-05-24
readingTime: "4 min read"
plans: "All plans"
---

# What is Flowamaz?

Flowamaz is an AI-native workflow orchestration platform. You describe what you want
in plain English, watch the platform build the workflow for you, and trust it to run
reliably — for years, surviving server restarts, network failures, and deployments.

It sits in a gap no other tool fills: AI-native like nothing before it, durable like
Temporal, visual and accessible like Zapier, and loved by developers like a real
engineering tool.

## Who it's for

- **Business teams** automate purchase approvals, employee onboarding, compliance
  reviews, and document routing — without writing code or learning BPMN.
- **Engineering teams** build AI agent pipelines, data workflows, and integration
  flows with durable execution, Git versioning, and a full CLI.

## Key concepts

- **Workflow** — a reusable definition of a process, stored as YAML, a visual canvas,
  and a plain-English description.
- **Instance** — one execution of a workflow. Each instance is pinned to a Git commit
  forever, so you always know exactly which version ran.
- **Node** — a single step. There are six types: Trigger, Action, AI, Human Gate,
  Router, and End.
- **Connector** — an integration with an external system (Slack, a database, an API).
- **Workspace** — an isolation boundary for a team or project. Nothing leaks between
  workspaces.
- **Environment** — dev, staging, or production inside a workspace.
- **Credential** — an encrypted API key or token stored in the workspace vault. Values
  are never shown again after you save them.
- **Human Gate** — a checkpoint that pauses a running workflow until a person approves
  or rejects it.

## The three editions

- **Community** — free, self-hosted. The full engine and CLI for individual developers
  and evaluation.
- **Cloud** — managed SaaS with teams, all AI providers, SSO, and support. Plans from
  $49/month.
- **Enterprise Self-Hosted** — runs on your own infrastructure with data residency and
  customer-managed encryption keys, for regulated organisations.

## Your first 5 minutes: creation to execution

1. **Describe it.** Type "When a purchase request over $5,000 comes in, get the manager
   to approve it, then create a PO." Flowamaz generates the workflow.
2. **Review the canvas.** The platform draws the steps as connected nodes. Adjust
   anything by dragging or editing.
3. **Add a Human Gate.** The manager approval becomes a gate that pauses the run.
4. **Trigger an instance.** Run it once with test data and watch each step light up in
   real time.
5. **Publish.** Commit your workflow to Git and promote it to production.

That's the whole loop: describe, review, run, watch, ship. Everything else in Flowamaz
makes that loop more powerful.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/getting-started/what-is-flowamaz.md)
