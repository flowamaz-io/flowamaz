---
title: Workflow YAML Format
updated: 2026-05-30
readingTime: "4 min read"
plans: "All plans"
---

# Workflow YAML Format

Every Flowamaz workflow is stored as a `flowamaz/v1` document with three top-level
sections: `workflow`, `nodes`, and `edges`. You can edit it directly in the YAML
editor, and the canvas stays in sync.

## Schema

```yaml
flowamaz: v1
workflow:
  id: purchase-approval
  version: 3
  name: Purchase Approval
nodes:
  - id: start
    type: Trigger
  - id: approve
    type: HumanGate
  - id: create-po
    type: Action
  - id: done
    type: End
edges:
  - id: e1
    from: start
    to: approve
  - id: e2
    from: approve
    to: create-po
    condition: "{{approve.decision}} == 'approved'"
  - id: e3
    from: create-po
    to: done
```

## `workflow`

| Key | Description |
|-----|-------------|
| `id` | Stable identifier for the workflow. |
| `version` | Integer bumped on each change. |
| `name` | Human-readable name shown in the UI. |

## `nodes`

Each node has an `id` (unique within the workflow) and a `type`. Available types:

- **Trigger** — entry point (manual, webhook, API, or schedule).
- **Action** — calls a connector operation.
- **AI** — runs a model call.
- **HumanGate** — pauses for a person to approve, reject, or submit input.
- **Router / IfElse / Switch** — branch on conditions.
- **Parallel** — run branches concurrently.
- **Wait** — pause for a duration or until a time.
- **ForEach** — iterate over a list.
- **While** — loop while a condition holds.
- **TryCatch** — handle errors in a sub-graph.
- **SubWorkflow** — call another workflow.
- **End** — terminal node.

## `edges`

Each edge has an `id`, a `from` node, and a `to` node. Add an optional `condition`
(an expression that must evaluate true) to take that edge only when it matches — this
is how branching nodes route execution.

Validation runs against this schema before you can publish, so malformed YAML is
caught early.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/workflows/yaml-format.md)
