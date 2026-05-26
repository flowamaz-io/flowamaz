# Trigger Nodes

A **Trigger** node is the entry point of every workflow. Every workflow must have exactly one trigger node.

## What it does
Defines the event or schedule that starts a new workflow instance. Until the trigger fires, no instance exists.

## Config fields
| Field | Required | Description |
|-------|----------|-------------|
| `type` | Yes | Trigger type: `manual`, `schedule`, `webhook`, `event` |
| `schedule` | Conditional | Cron expression (required when type=schedule) |
| `event` | Conditional | Event name to listen for (required when type=event) |

## YAML Example
```yaml
- id: start
  type: trigger
  label: "Customer submits form"
  config:
    type: webhook
    method: POST
```

## Common mistakes
- **Missing trigger node** — every workflow requires exactly one trigger. The validator raises a `W001` error if absent.
- **Two triggers in one workflow** — only one trigger is allowed. Split your workflow or use events.
- **Schedule without cron** — when type=schedule, the `schedule` field must be a valid cron expression (e.g. `0 9 * * 1-5`).
