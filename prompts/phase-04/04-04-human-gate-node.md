---
prompt-id: 04-04-human-gate-node
phase: 04
sequence: 4
roles: [Executor, Verifier, UX]
type: feature
depends-on: [04-03-ai-node-worker]
estimated-complexity: High
---

# Human Gate Node — Full Approval Flow (Slack + Teams + Email + Portal)

## Context
AI nodes work. Now build the human gate node worker — the pause-and-approve pattern
that makes Flowamaz suitable for enterprise compliance workflows. Slack and Teams
get first-class treatment with interactive approval buttons.

## Objective
Full human gate execution: delivery via Slack/Teams/Email/Portal, approval portal UI,
timeout escalation, and COO override. Both Slack and Teams have identical feature parity.

## Scope

### What to Build

**Human Gate Node Worker:**

`HumanGateNodeWorker : INodeWorker` (SupportedType = HumanGate)

ExecuteAsync:
1. Resolve assignee: evaluate `assigned_to_variable` → look up OrgUser by email
2. Create GateDecision record (status=Pending)
3. Deliver approval request via configured channel:
   - Slack: post interactive message with Approve/Reject buttons
   - Teams: post Adaptive Card with Approve/Reject actions
   - Email: send formatted email with approval links
   - Portal: no delivery (user checks /gates in the app)
4. Pause instance: set status=Waiting, release worker lease
5. Instance re-queued when gate is decided (GateDecisionController calls orchestrator)

**Slack Delivery:**

Use SlackConnectorHandler to send an interactive Block Kit message:
```json
{
  "blocks": [
    { "type": "header", "text": { "type": "plain_text", "text": "Approval Required" } },
    { "type": "section", "text": {
      "type": "mrkdwn",
      "text": "*{workflow_name}* requires your approval\n\n*Details:*\n{gate_summary}"
    }},
    { "type": "actions", "elements": [
      { "type": "button", "text": { "type": "plain_text", "text": "✓ Approve" },
        "style": "primary", "action_id": "gate_approve",
        "value": "{gate_decision_id}" },
      { "type": "button", "text": { "type": "plain_text", "text": "✗ Reject" },
        "style": "danger", "action_id": "gate_reject",
        "value": "{gate_decision_id}" }
    ]}
  ]
}
```

Slack action handler: `POST /api/v1/integrations/slack/actions`
Validates Slack signing secret → extracts action_id + value → calls GateDecisionService.

**Teams Delivery:**

Use Microsoft Teams connector to send an Adaptive Card:
```json
{
  "type": "AdaptiveCard",
  "version": "1.3",
  "body": [
    { "type": "TextBlock", "size": "Large", "weight": "Bolder", "text": "Approval Required" },
    { "type": "TextBlock", "text": "{gate_summary}", "wrap": true }
  ],
  "actions": [
    { "type": "Action.Http", "title": "Approve",
      "url": "{platform_url}/api/v1/gates/{gate_id}/decide",
      "method": "POST", "body": "{\"decision\":\"approved\"}" },
    { "type": "Action.Http", "title": "Reject",
      "url": "{platform_url}/api/v1/gates/{gate_id}/decide",
      "method": "POST", "body": "{\"decision\":\"rejected\"}" }
  ]
}
```

**Email Delivery:**

Use IEmailService to send approval request:
- Subject: "Approval Required: {workflow_name} — {gate_label}"
- Body: formatted HTML with instance details, approve/reject links
- Links: {platform_url}/gates/{gateDecisionId}/approve and /reject
  These are pre-authenticated one-click URLs (signed with HMAC, 72h expiry)

**Portal Gate Decision:**

Existing GET /gates endpoint shows pending gates.
Update decision flow: POST /gates/{instanceId}/{nodeId}/decide → orchestrator resumes instance.

**Approval Portal (frontend):**

`GatesView (/gates)` — new view:
- Table: workflow name, gate label, assignee, submitted at, expires at, actions
- [Approve] [Reject] buttons per row
- Note input field (optional)
- Filter: pending / all
- Badge on sidebar: pending gate count (live via polling)

**One-click email approval links:**

`GET /api/v1/gates/{gateDecisionId}/approve?sig={hmac_signature}`
`GET /api/v1/gates/{gateDecisionId}/reject?sig={hmac_signature}`
- Validates HMAC signature (GATE_SIGNING_KEY env var + gateDecisionId + decision + expiry)
- Records decision, redirects to success page
- Works without login (for email recipients who may not be app users)

**COO Override:**

WorkspaceAdmin can decide any pending gate regardless of assignment.
POST /gates/{instanceId}/{nodeId}/decide with WorkspaceAdmin role — no assignment check.

**Unit tests:**
- HumanGateNodeWorker: creates GateDecision, pauses instance (status=Waiting)
- HumanGateNodeWorker: Slack delivery builds correct Block Kit JSON
- HumanGateNodeWorker: Teams delivery builds correct Adaptive Card JSON
- GateDecisionService: approve → instance resumed → InstanceStarted event appended
- GateDecisionService: reject → instance failed → saga triggered if configured
- Email link HMAC: valid signature → decision recorded, expired → 400

## Technical Requirements
- [ ] Slack and Teams both deliver approval message on HumanGate execution
- [ ] Email approval links HMAC-signed, 72h expiry
- [ ] Portal: pending gates count shown in sidebar as badge
- [ ] WorkspaceAdmin can decide any gate (COO override)
- [ ] Gate timeout: GateTimeoutJob (built Phase 2) handles expiry
- [ ] Sensitive variables NOT included in gate summary sent to Slack/Teams/Email

## Acceptance Criteria
- [ ] HumanGate execution → GateDecision created, instance status=Waiting
- [ ] Slack message: approve button → Slack action → gate decided → instance resumes
- [ ] Teams card: approve action → gate decided
- [ ] Email link: valid HMAC → approve/reject works without app login
- [ ] GatesView: pending gates table, approve/reject works from portal

## Output Expected
```
backend/Flowamaz.Application/Workflow/Workers/HumanGateNodeWorker.cs
backend/Flowamaz.Application/Connectors/Gates/GateDecisionService.cs
backend/Flowamaz.Api/Controllers/GateApprovalController.cs (email links)
backend/Flowamaz.Api/Controllers/SlackActionsController.cs
backend/Flowamaz.Tests.Unit/Workflow/Workers/HumanGateNodeWorkerTests.cs
backend/Flowamaz.Tests.Unit/Connectors/Gates/GateDecisionServiceTests.cs
web/src/views/gates/GatesView.vue
web/src/views/gates/GateApprovalSuccessView.vue (redirect target for email links)
```
