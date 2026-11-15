# Flowamaz Workflow YAML Specification — v1

This document is the authoritative reference for the `flowamaz/v1` workflow YAML format.
Every field is listed with its type, valid values, and a usage example.

---

## Top-Level Structure

```yaml
apiVersion: flowamaz/v1     # Required. Must be exactly "flowamaz/v1"
kind: Workflow               # Required. Must be exactly "Workflow"
metadata: ...                # Required. Workflow identity
spec: ...                    # Required. Workflow definition
```

---

## metadata

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Unique slug. Lowercase letters, digits, hyphens. e.g. `purchase-approval` |
| `name` | string | Yes | Human-readable name. e.g. `"Purchase Request Approval"` |
| `description` | string | No | What this workflow does and who uses it |
| `version` | string | No | Semantic version. e.g. `"1.0.0"` |
| `workspace` | string | No | Workspace slug. e.g. `ws_finance` |

---

## spec

### spec.trigger (Required)

```yaml
spec:
  trigger:
    type: webhook       # Required. One of: webhook, form, schedule, manual, sub-workflow
    config: {}          # Optional. Type-specific configuration
```

**Trigger types:**
- `webhook` — triggered by an inbound HTTP POST
- `form` — triggered when a user submits a form
- `schedule` — triggered on a cron schedule (config: `{ cron: "0 9 * * 1-5" }`)
- `manual` — triggered manually by an operator
- `sub-workflow` — triggered when a parent workflow calls this as a sub-workflow

### spec.default_model (Optional)

```yaml
spec:
  default_model: claude-haiku-4-5
```

Sets the default AI model for all AI nodes in this workflow (F4 function).
Overrides workspace/org defaults. Can be overridden at the node level.

### spec.sla_threshold_ms (Optional)

```yaml
spec:
  sla_threshold_ms: 172800000   # 48 hours in milliseconds
```

Maximum allowed duration for a workflow instance. Exceeded instances are flagged for SLA breach alerts.

### spec.variables (Optional)

Declares variables available throughout the workflow.

```yaml
spec:
  variables:
    amount:
      type: number        # Required. One of: string, number, boolean, object, array
      required: true      # Optional. Default: false
      sensitive: false    # Optional. Sensitive vars are encrypted at rest. Default: false
      default: 0          # Optional. Default value if not provided at trigger time
```

---

## spec.nodes (Required)

A list of node definitions. At least one trigger node and one end node are required.

### Common Node Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Unique node ID. Lowercase letters, digits, hyphens. Referenced in edges. |
| `type` | string | Yes | Node type (see types below) |
| `label` | string | Yes | Display name shown on canvas |
| `config` | object | No | Type-specific configuration |
| `retry` | object | No | Retry policy (action and ai nodes) |
| `timeout` | object | No | Timeout policy |
| `compensate` | object | No | Saga compensation config |
| `position` | object | No | Canvas layout hint `{ x: 120, y: 80 }` |

### Node Types

| Type | Colour | Purpose |
|------|--------|---------|
| `trigger` | Purple | Entry point. Exactly one per workflow. |
| `action` | Teal | Execute an integration step (HTTP call, DB query) |
| `ai` | Blue | LLM inference step |
| `human-gate` | Amber | Human approval or decision checkpoint |
| `router` | Coral | Conditional branching (evaluates expressions) |
| `end` | Gray | Terminal state. At least one required. |
| `foreach` | — | Iterate over a list |
| `parallel` | — | Execute branches concurrently |
| `try-catch` | — | Error handling block |
| `wait` | — | Pause for a duration or until a condition |
| `sub-workflow` | — | Call another workflow |
| `annotation` | Yellow | Sticky note. Ignored by engine. |

### action node config

```yaml
- id: create-po
  type: action
  label: "Create PO in SAP"
  config:
    connector_id: sap-s4hana
    operation: create_purchase_order
    inputs:
      amount: "{{ variables.amount }}"
      requester: "{{ variables.requester_email }}"
```

### ai node config

```yaml
- id: classify-request
  type: ai
  label: "Classify Request"
  config:
    model: claude-haiku-4-5      # Optional. Overrides default_model
    prompt: "Classify this request: {{ variables.request_text }}"
    output_variable: classification
    output_schema:
      type: object
      properties:
        category: { type: string }
        confidence: { type: number }
```

### human-gate node config

```yaml
- id: manager-gate
  type: human-gate
  label: "Manager Approval"
  config:
    assigned_to_variable: manager_email
    delivery_channel: slack        # One of: slack, teams, email, portal
    timeout_hours: 48
    escalate_to_variable: director_email
    form_fields:
      - name: approved
        type: boolean
        required: true
      - name: note
        type: string
        required: false
```

**Best practice:** Always set `timeout_hours` on human gates. Without a timeout, the gate waits forever.

### router node config

```yaml
- id: amount-router
  type: router
  label: "Route by Amount"
  config:
    outputs:
      - id: high-value
        condition: "{{ variables.amount }} > 5000"
        label: "High value (>5000)"
      - id: low-value
        condition: "{{ variables.amount }} <= 5000"
        label: "Low value (≤5000)"
```

Edges from router nodes use `via:` to reference an output id.

### annotation node

```yaml
- id: note-1
  type: annotation
  label: "Finance team note"
  content: "This step validates against BambooHR employee records"
  position: { x: 120, y: 80 }
```

Annotations are ignored by the execution engine.

### retry policy

```yaml
retry:
  max_attempts: 3       # 1-100. Best practice: ≤10
  backoff_seconds: 5    # Initial wait between retries
  backoff_multiplier: 2.0  # Exponential backoff multiplier
```

### timeout policy

```yaml
timeout:
  seconds: 30
  on_timeout: timeout-handler   # Node ID to jump to on timeout, or null to fail
```

### compensation block

```yaml
compensate:
  strategy: backward   # One of: backward, forward, pivot
  node_id: undo-create-po   # Compensation action node ID
```

---

## spec.edges (Required)

Directed connections between nodes.

```yaml
edges:
  - id: e1
    from: validate-employee    # Source node ID
    to: amount-router          # Target node ID
  - id: e2
    from: amount-router
    to: manager-gate
    via: high-value            # Router output ID (required for router source nodes)
```

---

## spec.groups (Optional)

Visual swimlane groupings. Not interpreted by the engine.

```yaml
groups:
  - id: approval-stage
    label: "Approval Stage"
    node_ids: [manager-gate, cfo-gate]
    color: "#EF9F27"
```

---

## Expressions

Use `{{ expression }}` syntax to reference variables and system values.

**Declared variables:** `{{ variables.amount }}`, `{{ variables.requester_email }}`

**System variables:**

| Variable | Description |
|----------|-------------|
| `workflow.id` | Workflow definition ID |
| `workflow.name` | Workflow name |
| `instance.id` | Current instance ID |
| `node.id` | Current node ID |
| `now` | Current ISO timestamp |
| `env.*` | Environment variables (e.g. `env.API_BASE_URL`) |

**Example:**

```yaml
condition: "{{ variables.amount }} > 5000"
message: "Instance {{ instance.id }} started at {{ now }}"
```

---

## Validation Error Codes

| Code | Layer | Severity | Description |
|------|-------|----------|-------------|
| SCH-* | 1 | Error | JSON Schema violation |
| GRF-001 | 2 | Error | Wrong number of trigger nodes |
| GRF-002 | 2 | Error | Edge references non-existent node |
| GRF-003 | 2 | Error | Node is unreachable from trigger (orphaned) |
| GRF-004 | 2 | Error | No end node defined |
| GRF-005 | 2 | Error | Router edge `via` references undeclared output |
| EXP-001 | 3 | Warning | Expression references undeclared variable |
| CAP-001 | 4 | Info | AI node using default model |
| SEC-001 | 5 | Error | Potential hardcoded secret detected |
| SEC-002 | 5 | Error | External URL in annotation content |
| BPR-001 | 6 | Warning | Human gate has no timeout |
| BPR-002 | 6 | Warning | Retry max_attempts exceeds 10 |
| BPR-003 | 6 | Warning | Workflow has no description |
| BPR-004 | 6 | Info | No SLA threshold configured |

Errors (layers 1-5) block workflow saves. Warnings and Info do not.

---

## Complete Example

```yaml
apiVersion: flowamaz/v1
kind: Workflow
metadata:
  id: purchase-approval
  name: "Purchase Request Approval"
  description: "Routes purchase requests for manager and CFO approval"
  version: "1.0.0"
  workspace: ws_finance

spec:
  trigger:
    type: webhook
    config:
      verify_signature: true

  default_model: claude-haiku-4-5
  sla_threshold_ms: 172800000   # 48h

  variables:
    amount:
      type: number
      required: true
      sensitive: false
    requester_email:
      type: string
      required: true
      sensitive: false

  nodes:
    - id: start
      type: trigger
      label: "Purchase Request Received"

    - id: validate-employee
      type: action
      label: "Validate Employee"
      config:
        connector_id: bamboohr
        operation: get_employee
        inputs:
          email: "{{ variables.requester_email }}"
      retry:
        max_attempts: 3
        backoff_seconds: 5
        backoff_multiplier: 2.0

    - id: amount-router
      type: router
      label: "Route by Amount"
      config:
        outputs:
          - id: high-value
            condition: "{{ variables.amount }} > 5000"
            label: "High value"
          - id: low-value
            condition: "{{ variables.amount }} <= 5000"
            label: "Low value"

    - id: manager-gate
      type: human-gate
      label: "Manager Approval"
      config:
        assigned_to_variable: manager_email
        delivery_channel: slack
        timeout_hours: 48
        form_fields:
          - name: approved
            type: boolean
            required: true

    - id: done
      type: end
      label: "Done"

  edges:
    - id: e1
      from: start
      to: validate-employee
    - id: e2
      from: validate-employee
      to: amount-router
    - id: e3
      from: amount-router
      to: manager-gate
      via: high-value
    - id: e4
      from: amount-router
      to: done
      via: low-value
    - id: e5
      from: manager-gate
      to: done
```
