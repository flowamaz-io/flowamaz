---
prompt-id: 03-01-yaml-spec-validator
phase: 03
sequence: 1
roles: [Executor, Verifier]
type: feature
depends-on: []
estimated-complexity: High
---

# Flowamaz YAML Specification + 6-Layer Validator

## Context
Phase 2 built the SFG parser that validates graph structure. Phase 3 needs a complete,
documented YAML specification and a richer 6-layer validator that powers the CodeMirror
editor's real-time feedback. This is the foundation all creation methods produce output for.

## Objective
Define the complete Flowamaz workflow YAML specification as a JSON Schema, implement
the 6-layer validator (schema, graph, expression, capability, security, best-practice),
expose it as an API endpoint, and produce the validator NuGet-compatible library that
the fmz CLI (Phase 5) will also use.

## Scope

### What to Build

**Complete YAML specification (docs/yaml-spec.md + JSON Schema):**

Define the canonical FlowAmaz workflow YAML format:

```yaml
# flowamaz-workflow.yaml
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
    type: webhook | form | schedule | manual | sub-workflow
    config: {}            # type-specific config

  default_model: claude-haiku-4-5     # optional F4 default
  sla_threshold_ms: 172800000         # optional SLA (48h)

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
    - id: validate-employee
      type: action | ai | human-gate | router | end | foreach |
             parallel | try-catch | wait | sub-workflow | annotation
      label: "Validate Employee"
      config: {}            # type-specific config
      retry:
        max_attempts: 3
        backoff_seconds: 5
        backoff_multiplier: 2.0
      timeout:
        seconds: 30
        on_timeout: timeout-handler   # node id or null
      compensate:
        strategy: backward | forward | pivot
        node_id: compensate-validate  # compensation node id

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
            label: "Low value"

    - id: manager-gate
      type: human-gate
      label: "Manager Approval"
      config:
        assigned_to_variable: "manager_email"
        delivery_channel: slack | teams | email | portal
        timeout_hours: 48
        escalate_to_variable: "director_email"
        form_fields:
          - name: approved
            type: boolean
            required: true
          - name: note
            type: string
            required: false

    - id: annotation-1
      type: annotation
      label: "Finance team note"
      content: "This step validates against BambooHR"
      position: { x: 120, y: 80 }     # canvas layout hint

  edges:
    - id: e1
      from: validate-employee
      to: amount-router
    - id: e2
      from: amount-router
      to: manager-gate
      via: high-value                  # output id from router

  groups:                              # swimlane groupings (Phase 3 canvas)
    - id: approval-stage
      label: "Approval Stage"
      node_ids: [manager-gate, cfo-gate]
      color: "#EF9F27"
```

**JSON Schema (backend/Flowamaz.Core/Schemas/workflow-v1.schema.json):**
Full JSON Schema covering all node types, all config shapes per type,
all valid enum values. Used by NJsonSchema for validation.

**6-Layer Validator (Flowamaz.Application/Workflow/Validation/):**

`IWorkflowValidator` + `WorkflowValidator`:
`ValidateAsync(yamlContent, workspaceId?, ct)` → ValidationResult

Layer 1 — Schema validation:
- NJsonSchema validates against workflow-v1.schema.json
- Returns field-level errors with line numbers from YAML parser

Layer 2 — Graph structure validation:
- Exactly one trigger node
- All edge from/to reference existing node IDs
- All router output via IDs reference declared output IDs
- No orphaned nodes (reachable from trigger)
- At least one End node
- No cycles (except while loops — flag if detected without while block)

Layer 3 — Expression validation:
- All `{{ expression }}` patterns reference declared variables or known system vars
- System vars: workflow.id, workflow.name, instance.id, node.id, now, env.*
- Unknown variable reference → warning (not error — may be set at runtime)

Layer 4 — Capability validation:
- AI nodes: model must have required capability (vision for F3, long ctx for F7)
- Uses ModelCatalogue from database (if workspaceId provided)
- If no workspaceId: skip capability check

Layer 5 — Security validation:
- No hardcoded secrets in config values (regex patterns for common secret formats)
- No external URLs in annotation content (XSS vector)
- Connector network domains must be declared

Layer 6 — Best practice validation (warnings, not errors):
- Human gate with no timeout → warning "Gate has no timeout configured"
- Retry with max_attempts > 10 → warning "High retry count may cause long delays"
- No description on workflow → warning "Add a description to improve discoverability"
- SLA not set → info "No SLA threshold configured"

**ValidationResult model:**
```csharp
record ValidationResult(
    bool IsValid,
    IReadOnlyList<ValidationIssue> Errors,
    IReadOnlyList<ValidationIssue> Warnings,
    IReadOnlyList<ValidationIssue> Info
);

record ValidationIssue(
    int Layer,            // 1-6
    string Code,          // e.g. "SCH-001", "GRF-003", "SEC-001"
    string Message,
    string? NodeId,
    int? Line, int? Column
);
```

**API endpoint:**
`POST /api/v1/workspaces/{workspaceId}/workflows/validate`
Body: { yaml_content: string }
[RequireWorkspaceRole(Designer)]
Returns: ValidationResult (200 always — validation errors are in the body, not HTTP status)

**Validator NuGet preparation:**
Structure the validator so it can be extracted to a standalone NuGet package
(Flowamaz.Validator) with no Infrastructure dependencies — only Core.
The fmz CLI will use this package in Phase 5 via `fmz validate`.

**Unit tests:**
- Valid YAML → IsValid=true, 0 errors
- Missing trigger node → Layer 2 error GRF-001
- Unknown edge target → Layer 2 error GRF-002
- Orphaned node → Layer 2 error GRF-003
- Hardcoded secret pattern in config → Layer 5 error SEC-001
- Missing gate timeout → Layer 6 warning BPR-001
- Unknown variable reference → Layer 3 warning EXP-001

## Technical Requirements
- [ ] JSON Schema covers all node types and config shapes
- [ ] Validator returns line/column numbers for schema errors
- [ ] Layer 2 graph checks run even if Layer 1 has errors (report all issues at once)
- [ ] Capability check (Layer 4) skipped gracefully if no workspaceId
- [ ] Secret regex patterns: detect API keys, tokens, passwords in config values
- [ ] ValidationResult: errors block save, warnings do not
- [ ] API endpoint: 200 always, IsValid field indicates result
- [ ] Validator has no Infrastructure dependencies (pure Core + Application)
- [ ] dotnet build — 0 errors, 0 warnings
- [ ] dotnet test — all pass, validator tests cover all 6 layers

## Acceptance Criteria
- [ ] Valid YAML → IsValid=true in API response
- [ ] YAML with orphaned node → Layer 2 error with node ID in message
- [ ] YAML with hardcoded "sk-" prefix value → Layer 5 security error
- [ ] YAML with gate and no timeout → Layer 6 warning (IsValid still true)
- [ ] docs/yaml-spec.md exists and documents every field

## Output Expected
```
backend/Flowamaz.Core/Schemas/workflow-v1.schema.json
backend/Flowamaz.Core/Interfaces/Workflow/IWorkflowValidator.cs
backend/Flowamaz.Application/Workflow/Validation/WorkflowValidator.cs
backend/Flowamaz.Application/Workflow/Validation/ValidationResult.cs
backend/Flowamaz.Api/Controllers/WorkflowValidationController.cs
backend/Flowamaz.Tests.Unit/Workflow/WorkflowValidatorTests.cs (one test per layer + combinations)
docs/yaml-spec.md
```
