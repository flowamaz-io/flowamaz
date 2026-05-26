# Human Gate Nodes

A **Human Gate** node pauses the workflow and waits for a person to approve, reject, or provide input.

## What it does
When execution reaches a human gate, the instance is suspended and one or more assignees receive a notification. The workflow resumes only after an assignee takes action — approve, reject, or submit a form. You can configure a timeout so the workflow escalates or auto-decides if no one acts.

## Config fields
| Field | Required | Description |
|-------|----------|-------------|
| `assignees` | Yes | List of user IDs, role names, or `{{variable}}` expressions. |
| `title` | No | Human-readable task title shown in the gate inbox. |
| `description` | No | Detailed instructions for the assignee. |
| `form` | No | Optional form schema for collecting structured input from the assignee. |
| `timeoutSeconds` | No | Seconds to wait before triggering `onTimeout` branch. Default: no timeout. |
| `onTimeout` | No | Action on timeout: `auto-approve`, `auto-reject`, or a node ID to jump to. |
| `allowedRoles` | No | Restrict gate completion to users with these workspace roles. |

## YAML Example
```yaml
- id: manager-approval
  type: human-gate
  label: "Manager approval"
  config:
    assignees:
      - "{{workflow.variables.manager_id}}"
    title: "Approve expense claim {{input.amount}}"
    timeoutSeconds: 172800
    onTimeout: auto-reject
```

## Common mistakes
- **No assignees** — the gate will pend forever with no one to approve it. Always set at least one assignee.
- **No timeout in production** — without a timeout, a human gate can block a workflow indefinitely if an assignee is unavailable.
- **Variable assignees not set** — if you use `{{workflow.variables.manager_id}}`, make sure a prior node sets that variable.
