---
prompt-id: fix-07-01-critical-high
phase: fix-phase-07
sequence: 1
roles: [Executor, Verifier, Security, Testing]
type: fix
source-issue: Verifier — Phase 07 report Critical + High
severity: Critical
depends-on: []
---

# Fix: Template YAML Format + DTO Validation + Audit Actor

## Issues Being Fixed

```
CRITICAL — Installed templates cannot publish or run
FILE: WorkflowTemplateSeeder.cs (seeded YAML uses spec.nodes) vs SfgParser.cs (reads root-level nodes)
FIX: Update SfgParser/RootDto to read spec.nodes/spec.edges (aligns runtime with documented flowamaz/v1 schema)
```

```
HIGH — 6 new request DTOs lack FluentValidation; Stripe URLs unvalidated (open-redirect surface)
FILE: BillingController.cs, TemplatesController.cs, InstanceDebugController.cs
FIX: Add validators; allow-list Stripe redirect URLs
```

```
HIGH — instance.started audit event has no actor_user_id
FILE: WorkflowOrchestrator.cs TriggerAsync
FIX: Thread user id into TriggerAsync or set actor_type=system when no user id available
```

--- FIX 1: Unify YAML format — update SfgParser to read spec.nodes ---

In backend/Flowamaz.Core/Workflow/SfgParser.cs:

The parser currently reads root-level nodes/edges:
  var nodes = root.Nodes  (where root is the root YAML object)

Update RootDto to read from spec.nodes/spec.edges:

```csharp
// RootDto (or equivalent deserialization target):
public class WorkflowRoot
{
    public string ApiVersion { get; set; }
    public string Kind { get; set; }
    public WorkflowMetadata Metadata { get; set; }
    public WorkflowSpec Spec { get; set; }
    
    // Legacy support: root-level nodes/edges (backward compat)
    public List<NodeDto>? Nodes { get; set; }
    public List<EdgeDto>? Edges { get; set; }
}

public class WorkflowSpec
{
    public TriggerDto? Trigger { get; set; }
    public List<NodeDto>? Nodes { get; set; }
    public List<EdgeDto>? Edges { get; set; }
    public Dictionary<string, VariableDto>? Variables { get; set; }
}
```

In SfgParser.Parse():
```csharp
// Read nodes from spec.nodes (preferred) or root-level nodes (legacy)
var nodes = root.Spec?.Nodes ?? root.Nodes ?? new List<NodeDto>();
var edges = root.Spec?.Edges ?? root.Edges ?? new List<EdgeDto>();

if (!nodes.Any())
    throw new SfgParseException("Workflow has no nodes. Add at least a Trigger node and one End node.");
```

This makes SfgParser read both formats:
- flowamaz/v1 with spec.nodes (new format — templates, NL generator, Co-pilot)
- Legacy root-level nodes (old format — existing Phase 2 executing workflows)

Both work. No existing workflows break. Templates now work end-to-end.

Verify: run existing integration tests — all must still pass.

Add integration test: install official template → publish → trigger:
In Phase7TemplateTests.cs add:
- InstallOfficialTemplate_PublishAndTrigger_InstanceCreated
  Install the "Purchase Approval" seeded template
  Publish the installed workflow (must succeed — no SfgParseException)
  Trigger a test instance
  Assert instance status is not Failed immediately

--- FIX 2: FluentValidation on 6 new DTOs + Stripe URL allowlist ---

Add validators for each new request DTO:

CheckoutRequest (BillingController):
- PlanId: NotEmpty, must be one of the seeded plan IDs
- SuccessUrl: NotEmpty, must start with allowed origin
- CancelUrl: NotEmpty, must start with allowed origin

Allowed origins for Stripe redirect URLs:
Read from config: PLATFORM_BASE_URL (e.g. https://app.flowamaz.io)
In Development: also allow http://localhost:8306, https://localhost:8443

Validator:
```csharp
RuleFor(x => x.SuccessUrl)
    .NotEmpty()
    .Must(url => IsAllowedRedirectUrl(url))
    .WithMessage("Redirect URL must be a valid Flowamaz app URL.");
```

IsAllowedRedirectUrl(string url):
- Parse as Uri
- Check host matches PLATFORM_BASE_URL host
- OR host is localhost (Development only)

PortalRequest: same URL validation on ReturnUrl

PublishTemplateRequest:
- Name: NotEmpty, MaxLength(100)
- Description: NotEmpty, MaxLength(500)
- Category: NotEmpty, must be one of the valid categories

InstallTemplateRequest:
- WorkflowName: NotEmpty, MaxLength(200)

ReplayRequest:
- PayloadOverride: valid JSON if provided (not required)

SetBreakpointRequest:
- WorkflowId: NotEmpty
- NodeId: NotEmpty

Register all validators in DI (same pattern as existing validators).

--- FIX 3: Audit actor_user_id on manual trigger ---

In backend/Flowamaz.Application/Workflow/Orchestrator/WorkflowOrchestrator.cs:

TriggerAsync signature currently:
```csharp
Task<WorkflowInstance> TriggerAsync(Guid workflowId, Guid workspaceId, 
    JsonElement payload, string? idempotencyKey, bool isTest, CancellationToken ct)
```

Add optional triggeredByUserId parameter:
```csharp
Task<WorkflowInstance> TriggerAsync(Guid workflowId, Guid workspaceId,
    JsonElement payload, string? idempotencyKey, bool isTest,
    Guid? triggeredByUserId, CancellationToken ct)
```

In WorkflowsController / PublicWorkflowsController / WebhookReceiveController:
- WorkflowsController: pass currentUser.GetUserId() as triggeredByUserId
- PublicWorkflowsController: pass null (API key trigger, no user)
- WebhookReceiveController: pass null (webhook trigger, no user)

In AuditService.RecordTriggerAsync:
```csharp
actor_type = triggeredByUserId.HasValue ? "user" : 
             isWebhook ? "webhook" : "api_key"
actor_user_id = triggeredByUserId
```

--- CLEANUPS (include in this prompt) ---

4. Delete stray file: git rm ".env copy.example"

5. TemplateService: replace InvalidOperationException with domain exceptions:
   Create TemplateException(string message, int statusCode) : AppException
   Throw TemplateException(404) for not found
   Throw TemplateException(422) for invalid state (not published, invalid YAML)
   Throw TemplateException(400) for bad input (missing name/category)

6. Replay endpoint: return actionable 404 body:
   return NotFound(new { error = "instance_not_found",
     message = "Instance not found in this workspace, or it is still running. Only terminal instances can be replayed." })

7. Dev debug endpoints: add workspace membership check:
   Before processing any dev breakpoint/resume/step request:
   var member = await _workspaceMemberRepo.GetMemberAsync(workspaceId, currentUserId, ct);
   if (member == null) return Forbid();

After all fixes:
- dotnet build — 0 errors 0 warnings
- dotnet test — all pass including new install→publish→trigger test
- grep "spec\.nodes\|spec\.edges" backend/Flowamaz.Core/Workflow/SfgParser.cs → confirms new read path

git add . && git commit -m "fix(critical): SfgParser reads spec.nodes; fix(high): DTO validation, Stripe URL allowlist, audit actor" && git push origin develop
