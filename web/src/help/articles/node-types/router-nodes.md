# Router Nodes

A **Router** node evaluates conditions and sends the workflow down one of several branches.

## What it does
Routes execution to different downstream nodes based on boolean CEL expressions evaluated in order. The first matching condition wins. If no condition matches and a default branch is defined, the default runs. If no condition matches and there is no default, the workflow raises a `RouterNoMatch` error.

## Config fields
| Field | Required | Description |
|-------|----------|-------------|
| `conditions` | Yes | List of `{ condition, target }` pairs evaluated top-to-bottom. |
| `conditions[].condition` | Yes | CEL boolean expression (e.g. `input.amount > 1000`). |
| `conditions[].target` | Yes | Node ID to route to when this condition is true. |
| `default` | No | Node ID to route to when no condition matches. |

## YAML Example
```yaml
- id: route-by-priority
  type: router
  label: "Route by priority"
  config:
    conditions:
      - condition: "ticket_priority == 'urgent'"
        target: escalate-to-team
      - condition: "ticket_priority == 'normal'"
        target: assign-to-queue
    default: low-priority-pool
```

## Common mistakes
- **No default branch** — if a value falls outside all conditions (e.g. unexpected enum), the workflow errors. Always add a default.
- **CEL syntax errors** — test expressions carefully. `==` not `=`. String literals need quotes. Use the validator to check before publishing.
- **Order matters** — conditions are evaluated top-to-bottom. Put the most specific conditions first.
