---
prompt-id: 04-07-connector-payload-mapper
phase: 04
sequence: 7
roles: [Executor, Verifier, UX]
type: feature
depends-on: [04-06-workflow-empathy]
estimated-complexity: Medium
---

# Connector Payload Auto-Mapper + Co-pilot Connector Patterns

## Context
Connectors and the AI node are working. Now build the "paste a sample payload"
auto-mapper (eliminates the most tedious part of connector setup) and extend the
Co-pilot pattern matcher with connector-specific patterns.

## Objective
Auto-mapper that generates field mappings from a pasted JSON sample. Extended Co-pilot
patterns for 10 connector-specific commands. Connector Co-pilot test coverage.

## Scope

### What to Build

**Payload Auto-Mapper (Flowamaz.Application/Connectors/PayloadAutoMapper.cs):**

`IPayloadAutoMapper` + `PayloadAutoMapper`:

`MapAsync(sampleJson, targetSchema, workspaceId, ct)` → AutoMapResult

Given:
- sampleJson: a JSON payload the user pastes from the external system
- targetSchema: the operation's input_schema from the connector manifest

Algorithm:
1. Parse sampleJson into JsonDocument
2. Extract all leaf paths: { "$.contact_id": "CON-001", "$.line_items[0].amount": 100 }
3. For each required field in targetSchema:
   - Find best matching path in sample using name similarity (Levenshtein distance)
   - Match: contactId ↔ contact_id, lineItems ↔ line_items (camelCase normalisation)
   - Suggest mapping as: {{ variables.contact_id }} (workflow variable)
4. Return: { mappings: [{fieldName, suggestedExpression, confidence}], unmapped: [...] }

This eliminates manual field mapping in the Node Inspector for Action nodes.

**API endpoint:**
`POST /api/v1/workspaces/{workspaceId}/connectors/{connectorId}/operations/{operationId}/auto-map`
Body: { sample_payload: object }
[RequireWorkspaceRole(Designer)]
Returns: AutoMapResult

**Frontend — "Paste a Sample Payload" in Node Inspector:**

In NodeInspector (Action node, after operation selected):
- "Paste a sample payload" link below the input fields
- Opens a textarea modal
- User pastes JSON from the external system
- Submit → POST /auto-map → fields auto-populated with suggested expressions
- User reviews and confirms (or overrides)
- Confidence shown as colour: green > 80%, amber 50-80%, red < 50%

**Extended Co-pilot Connector Patterns (10 new patterns):**

Add to CopilotPatternMatcher:
1. "Connect to Slack" → add Slack action node with workspace credential
2. "Send Slack message to {channel}" → configure Slack send-message operation
3. "Send Teams approval to {person}" → add Teams human-gate node
4. "Create a GitHub issue" → add GitHub create-issue action node
5. "Send email to {address}" → add email send-email action node
6. "Call {url} with GET/POST" → add HTTP connector node
7. "Query postgres for {table}" → add PostgreSQL query action node
8. "Read file from {path}" → add file-system read-file action node
9. "Schedule this workflow every {cron}" → configure schedule trigger
10. "Parse this document" → add SOP parsing sub-workflow reference

Each pattern generates the correct YAML patch to add the node with pre-configured
connector_id and operation_id fields.

**Unit tests:**
- PayloadAutoMapper: camelCase field matches snake_case schema field (high confidence)
- PayloadAutoMapper: exact match → confidence 1.0
- PayloadAutoMapper: no match → in unmapped list
- CopilotPatternMatcher: "Send Slack message to #finance" → Slack action node patch
- CopilotPatternMatcher: "Schedule every Monday 9am" → cron trigger patch

## Technical Requirements
- [ ] Auto-mapper: camelCase/snake_case normalisation for matching
- [ ] Auto-mapper: confidence scores 0.0-1.0 per field
- [ ] Co-pilot patterns: 10 new connector patterns returning correct YAML patches
- [ ] Node inspector: paste payload modal with auto-populated fields

## Acceptance Criteria
- [ ] POST /auto-map with Xero invoice sample → contact_id field mapped at confidence > 0.8
- [ ] CopilotPatternMatcher: "Send Slack message" → Slack connector action node in patch
- [ ] Node inspector: paste modal → fields populated → user can confirm

## Output Expected
```
backend/Flowamaz.Application/Connectors/PayloadAutoMapper.cs
backend/Flowamaz.Api/Controllers/ConnectorsController.cs (auto-map endpoint added)
backend/Flowamaz.Application/Workflow/Creation/CopilotPatternMatcher.cs (10 new patterns)
backend/Flowamaz.Tests.Unit/Connectors/PayloadAutoMapperTests.cs
backend/Flowamaz.Tests.Unit/Workflow/Creation/CopilotPatternMatcherTests.cs (extended)
web/src/components/editor/PayloadAutoMapperModal.vue
web/src/components/editor/NodeInspector.vue (paste payload link added)
```
