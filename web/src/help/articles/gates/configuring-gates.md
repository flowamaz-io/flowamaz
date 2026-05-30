---
title: Configuring Human Gates
updated: 2026-05-30
readingTime: "3 min read"
plans: "All plans"
---

# Configuring Human Gates

A Human Gate has three things to set up: **who** decides, **what** they can decide,
and **how long** they have. Configure them in the Node Inspector or in YAML.

## Assignee

Decide who the gate goes to. An assignee can be:

- A specific user.
- A workspace role (everyone with that role can act).
- A `{{variable}}` expression resolved at runtime — for example
  `{{workflow.variables.manager_id}}` set by an earlier node.

If you use a variable assignee, make sure a prior node actually sets that variable,
or the gate will have no one to notify.

## Options

Options are the choices the responder picks from. The default is approve / reject, but
you can define your own (for example *Approve*, *Reject*, *Request changes*). Each
option's value becomes available to the edges leaving the gate so you can route on it.

Add a form if you need structured input alongside the choice — a reason field, an
amount, a comment.

## SLA timeout

Set a timeout so a gate can't block a workflow forever. If no one responds within the
window, the gate fires its timeout behaviour — auto-approve, auto-reject, escalate to
another assignee, or jump to a specific node.

```yaml
- id: manager-approval
  type: HumanGate
  config:
    assignees:
      - "{{workflow.variables.manager_id}}"
    title: "Approve expense claim {{input.amount}}"
    options: [approve, reject]
    timeoutSeconds: 172800   # 48 hours
    onTimeout: auto-reject
```

**Always set a timeout in production.** Without one, an unavailable approver can stall
a workflow indefinitely.

## Tips

- Give the gate a clear **title** and **description** — that's what the responder sees.
- Prefer role assignees over single users so coverage survives someone being out.
- Keep timeouts realistic: long enough for a real decision, short enough to keep
  things moving.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/gates/configuring-gates.md)
