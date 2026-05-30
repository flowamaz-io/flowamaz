---
prompt-id: 08-01-deferred-fixes
phase: 08
sequence: 1
roles: [Executor, Verifier, Security]
type: feature
depends-on: []
estimated-complexity: Medium
---

# Deferred fix-07 Items — DTO Validators + Stripe Allowlist + Domain Exceptions + Breakpoint + UX

## Context
fix-07 deferred these items due to tool-delivery constraints. All are verified
as genuinely needed against the real codebase. Address all in one prompt.

## Scope

### FIX 1 — 6 DTO validators + Stripe redirect-URL allowlist

Add FluentValidation validators for all 6 new Phase 7 request DTOs.

Pattern: look at WorkflowInstancesController for how validators are registered and called.

CheckoutRequestValidator:
- PlanId: NotEmpty
- SuccessUrl: NotEmpty, Must(IsAllowedRedirectUrl)
- CancelUrl: NotEmpty, Must(IsAllowedRedirectUrl)

PortalRequestValidator:
- ReturnUrl: NotEmpty, Must(IsAllowedRedirectUrl)

IsAllowedRedirectUrl(string url):
- Parse as Uri, must succeed
- Host must match Platform:BaseUrl host config value
- OR host is localhost (IsDevelopment() check via IWebHostEnvironment)
- Returns false for anything else

PublishTemplateRequestValidator:
- Name: NotEmpty, MaxLength(100)
- Description: NotEmpty, MaxLength(500)
- Category: NotEmpty, must be in ValidCategories list

InstallTemplateRequestValidator:
- WorkflowName: NotEmpty, MaxLength(200)

ReplayRequestValidator:
- PayloadOverride: if provided, must be valid JSON (try JsonDocument.Parse)

SetBreakpointRequestValidator:
- WorkflowId: NotEmpty
- NodeId: NotEmpty

Register all validators in DI in Program.cs or the existing validator registration block.
Inject IValidator<T> into BillingController, TemplatesController, InstanceDebugController.
Call ValidateAndThrowAsync before processing.

### FIX 2 — TemplateException domain exception

In backend/Flowamaz.Application/Library/Services/TemplateService.cs:
Create TemplateException : AppException in Flowamaz.Core/Exceptions/

Throw with correct status codes:
- Template not found → TemplateException(404, "template_not_found", "Template not found.")
- Workflow not published → TemplateException(422, "workflow_not_published", "Publish the workflow before submitting as a template.")
- Invalid YAML → TemplateException(422, "invalid_yaml", "Template YAML failed validation.")
- Missing name/category → TemplateException(400, "validation_error", message)

Update TemplateServiceTests to assert correct exception types and status codes.

GlobalExceptionMiddleware already handles AppException subclasses — no middleware changes needed.

### FIX 3 — Breakpoint auto-pause in StepAsync

In backend/Flowamaz.Application/Workflow/Orchestrator/WorkflowOrchestrator.cs:
Find the per-node execution loop (StepAsync or equivalent).

Add pre-node breakpoint check:
```csharp
if (_environment.IsDevelopment() &&
    _breakpointRegistry.IsBreakpointSet(instance.WorkflowDefinitionId, currentNode.Id))
{
    instance.Status = WorkflowInstanceStatus.BreakpointHit;
    instance.CurrentNodeId = currentNode.Id;
    await _instanceRepository.UpdateAsync(instance, ct);
    _logger.LogInformation(
        "Breakpoint hit — instance {InstanceId} paused at node {NodeId}",
        instance.Id, currentNode.Id);
    return; // pause — resume via POST /api/v1/dev/instances/{id}/resume
}
```

Add WorkflowInstanceStatus.BreakpointHit to the enum.

Resume endpoint: on resume, clear the breakpoint for this execution only
(use a per-instance set in IBreakpointRegistry) and re-enter StepAsync.

Unit test:
- BreakpointRegistry.IsBreakpointSet returns true → StepAsync sets status=BreakpointHit
- Resume clears the per-instance pause and continues

### FIX 4 — Dev endpoint workspace membership check

In backend/Flowamaz.Api/Controllers/InstanceDebugController.cs:
Add workspace membership check before processing breakpoint/resume/step requests:

```csharp
var member = await _workspaceMemberRepository
    .GetMemberAsync(workspaceId, _currentUser.UserId, ct);
if (member == null)
    return Forbid();
```

### FIX 5 — Frontend UX polish

web/src/components/layout/Sidebar.vue:
Add FmTooltip to Gates nav badge:
content="Gates pause a workflow for a human decision. This badge shows approvals waiting on you."

web/src/views/gates/GatesView.vue:
Add CTA to empty state:
cta-label="Learn about human gates"
cta-href="https://docs.flowamaz.io/human-gates"

web/src/components/onboarding/ProductTour.vue:
Step 4 title: "Create your first workflow"
Step 4 description: "Choose how you want to build — describe it in plain English, upload a document, or start from a template."
Step 5 title: "Ready to automate"
Step 5 description: "Start from a pre-built template or explore on your own."
Remove all references to "canvas" from steps 4-5.

web/src/components/common/FmTooltip.vue:
Add mouseenter/mouseleave on tooltip bubble with 150ms close delay:
```typescript
let closeTimer: ReturnType<typeof setTimeout> | null = null
const startClose = () => { closeTimer = setTimeout(() => { isVisible.value = false }, 150) }
const cancelClose = () => { if (closeTimer) clearTimeout(closeTimer) }
```

After all fixes:
dotnet build — 0 errors 0 warnings
dotnet test — all pass
npm run build --prefix web — 0 TypeScript errors

git add . && git commit -m "fix(deferred): DTO validators, Stripe allowlist, TemplateException, breakpoint wiring, UX polish" && git push origin develop
