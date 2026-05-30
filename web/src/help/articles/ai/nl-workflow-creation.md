---
title: Creating Workflows from Plain English
updated: 2026-05-30
readingTime: "3 min read"
plans: "All plans"
---

# Creating Workflows from Plain English

Describe a process in plain English and Flowamaz generates a complete `flowamaz/v1`
workflow — nodes, edges, and conditions — that you can review and refine on the canvas.

## The creation form

Choose **Plain English** when creating a workflow and fill in:

| Field | What to put |
|-------|-------------|
| **Name** | A short name for the workflow. |
| **Description** | The process in your own words — the most important field. |
| **Trigger hint** *(optional)* | How it should start: manual, webhook, API, or schedule. |

Submit, and the platform proposes a draft workflow for you to review before saving.

## Writing a good description

The quality of the result tracks the quality of your description. Aim for clear steps
and decisions:

> When a purchase request over $5,000 comes in, ask the requester's manager to approve
> it. If approved, create a purchase order and notify the requester. If rejected,
> email the requester with the reason.

That description gives Flowamaz everything it needs: a trigger, a condition, a Human
Gate, branching, and actions on each branch.

## Tips for better results

- **Name the trigger.** Say whether it starts from a webhook, a schedule, an API call,
  or manually.
- **Spell out decisions.** Phrases like "if approved" or "if the amount is over X"
  become branching edges.
- **Mention the people.** "Get the manager to approve" produces a Human Gate.
- **List the systems.** Naming Slack, a database, or an API helps map steps to
  connectors.
- **Keep one process per workflow.** Split unrelated flows into separate workflows.

## After generation

The result is a **draft**. Review the canvas, adjust nodes, wire up real connectors and
credentials, test-run it, then publish. Use **Co-pilot** for small follow-up edits, or
regenerate from a revised description if the shape is off.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/ai/nl-workflow-creation.md)
