---
title: Key Concepts Glossary
updated: 2026-05-24
readingTime: "5 min read"
plans: "All plans"
---

# Key Concepts Glossary

Every term you'll meet in Flowamaz, in alphabetical order, each with a short definition
and an example.

### Co-pilot
The in-canvas AI assistant that suggests nodes, fixes, and next steps as you build.
*Example: while editing a workflow, Co-pilot suggests "add a retry on the payment
action" after spotting a flaky API call.*

### Connector
An integration with an external system, used by Action nodes.
*Example: the Slack connector lets a workflow post a message to a channel.*

### Credential
An encrypted API key or token stored in the workspace vault. Values are never shown
again after saving.
*Example: a `stripe_prod` credential holds your Stripe secret key; workflows reference
it by alias only.*

### Durable execution
The guarantee that a running workflow survives server restarts, crashes, and network
failures and resumes exactly where it left off.
*Example: a 3-day approval workflow keeps waiting even if the server is redeployed
twice.*

### End
The terminal node type. It marks where a path through the workflow finishes.
*Example: a "Rejected" branch ends in an End node that records the outcome.*

### Environment
A context inside a workspace: dev, staging, or production.
*Example: you test in dev, validate in staging, and run real work in production.*

### Exactly-once
The guarantee that each step runs once and only once, even after retries — enforced by
the event log, connector idempotency keys, and a database unique constraint.
*Example: a payment is never double-charged when a node retries.*

### Gate
A checkpoint that pauses execution until a condition is met or a person acts.

### Human Gate
A gate that pauses a workflow until a person approves or rejects it.
*Example: purchases over $5,000 pause for the finance manager's approval.*

### Instance
One execution of a workflow definition, pinned to a Git commit forever.
*Example: "Onboarding run #482" is one instance of the Onboarding workflow.*

### Library
The built-in catalogue of community connectors and workflow templates you can install.
*Example: install the "New hire onboarding" template from the Library as a starting
point.*

### Marketplace
The managed, curated catalogue of connectors and templates available on Cloud plans.

### Node
A single step in a workflow. There are six types: **Trigger** (what starts the
workflow), **Action** (API calls and database operations), **AI** (an LLM inference
step), **Human Gate** (an approval checkpoint), **Router** (conditional branching), and
**End** (a terminal state).
*Example: an AI node summarises an incoming email before a Router decides where to send
it.*

### Organisation
A company or customer account. It contains workspaces and owns billing and SSO.
*Example: "Acme Corp" is an organisation with separate Finance and HR workspaces.*

### SFG (Simple Flow Graph)
The visual canvas representation of a workflow — nodes connected by edges.
*Example: the SFG shows a Trigger flowing into an Action, then a Human Gate.*

### Workflow
A reusable process definition, stored as YAML, an SFG canvas, and a plain-English
description.
*Example: the "Purchase approval" workflow defines how requests are reviewed and
approved.*

### Workspace
An isolation boundary for a team or project. Data never crosses between workspaces.
*Example: the HR workspace's workflows and credentials are invisible to the Finance
workspace.*

### YAML
The text format Flowamaz uses to store workflow definitions, editable directly or
generated from plain English.
*Example: editing the YAML lets a developer fine-tune a node the canvas generated.*

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/getting-started/key-concepts-glossary.md)
