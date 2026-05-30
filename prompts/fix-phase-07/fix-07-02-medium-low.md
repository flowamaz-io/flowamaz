---
prompt-id: fix-07-02-medium-low
phase: fix-phase-07
sequence: 2
roles: [Executor, Verifier, UX, Testing]
type: fix
source-issue: Verifier/UX — Phase 07 report Medium + Low travel-forward
severity: Medium
depends-on: [fix-07-01-critical-high]
---

# Fix: Breakpoint Wiring + UX Polish + Tour Copy

## Issues Being Fixed

Medium/Low items from phase-07-report.md travel-forward list.

--- FIX 1: Wire breakpoint auto-pause into StepAsync ---

In backend/Flowamaz.Application/Workflow/Orchestrator/WorkflowOrchestrator.cs:
Find StepAsync (or equivalent per-node execution method).

Add breakpoint check before executing each node:

```csharp
// In StepAsync, before executing the node:
if (_environment.IsDevelopment() && 
    _breakpointRegistry.IsBreakpointSet(instance.WorkflowDefinitionId, node.Id))
{
    // Pause the instance
    instance.Status = WorkflowInstanceStatus.BreakpointHit;
    instance.CurrentNodeId = node.Id;
    await _instanceRepo.UpdateAsync(instance, ct);
    
    // Emit WebSocket event
    await _hubContext.Clients.Group(instance.Id.ToString())
        .SendAsync("breakpoint_hit", new { nodeId = node.Id, instanceId = instance.Id }, ct);
    
    // Return without executing — instance stays paused
    // Resume via POST /api/v1/dev/instances/{id}/resume
    return;
}
```

Add WorkflowInstanceStatus.BreakpointHit to the enum.

Resume endpoint (already exists): on resume, call StepAsync for the paused node
with breakpoint temporarily cleared for this execution only.

Unit test:
- BreakpointRegistry set → StepAsync pauses instance at that node
- Resume → execution continues to next node

--- FIX 2: Gates badge tooltip + empty state CTA ---

In web/src/components/layout/Sidebar.vue:
Add FmTooltip to the Gates nav item badge:

```vue
<FmTooltip content="Gates pause a workflow for a human decision. 
  The badge shows approvals waiting on you.">
  <span class="gates-badge">{{ pendingGatesCount }}</span>
</FmTooltip>
```

In web/src/views/gates/GatesView.vue:
Update empty state to include a CTA:

```vue
<FmEmptyState
  icon="CheckCircleIcon"
  title="No pending approvals"
  description="When a workflow reaches a human gate, it will appear here for your review."
  cta-label="Learn about human gates"
  cta-to="/help?article=human-gates"
/>
```

--- FIX 3: Fix tour steps 4-5 copy ---

In web/src/components/onboarding/ProductTour.vue:

Steps 4 and 5 reference the canvas and template gallery but spotlight
the dashboard creation methods block. Fix the copy to match:

Step 4 (spotlights creation methods block):
Title: "Create your first workflow"
Description: "Choose how you want to build — describe it in plain English,
upload a document, or start from a template below."

Step 5 (shows template suggestion):
Title: "Ready to automate"
Description: "Start from a pre-built template to get going in seconds,
or explore on your own."

Remove references to "canvas" in these steps since the canvas is not visible.

--- FIX 4: FmTooltip hover-keep-open ---

In web/src/components/common/FmTooltip.vue:
Add mouseenter listener on the tooltip bubble to prevent it closing
when user moves mouse from trigger to tooltip:

```vue
<div 
  v-if="isVisible"
  class="tooltip-bubble ..."
  @mouseenter="cancelClose"
  @mouseleave="startClose"
>
  {{ content }}
</div>
```

Add cancelClose() and startClose() with a 150ms delay:
```typescript
let closeTimer: ReturnType<typeof setTimeout> | null = null

const startClose = () => {
  closeTimer = setTimeout(() => { isVisible.value = false }, 150)
}

const cancelClose = () => {
  if (closeTimer) clearTimeout(closeTimer)
}
```

--- FIX 5: Security — Dev-gate breakpoint controller at route level ---

In backend/Flowamaz.Api/Controllers/InstanceDebugController.cs:
Add a route constraint or middleware that gates the entire controller
in non-Development environments:

```csharp
[ApiController]
[Route("api/v1/dev")]
[Authorize]
[ServiceFilter(typeof(DevelopmentOnlyFilter))]
public class InstanceDebugController : ControllerBase
```

DevelopmentOnlyFilter:
```csharp
public class DevelopmentOnlyFilter : IActionFilter
{
    private readonly IWebHostEnvironment _env;
    public DevelopmentOnlyFilter(IWebHostEnvironment env) => _env = env;
    
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!_env.IsDevelopment())
            context.Result = new NotFoundResult();
    }
    public void OnActionExecuted(ActionExecutedContext context) { }
}
```

Register in Program.cs: builder.Services.AddScoped<DevelopmentOnlyFilter>();

This gates the controller before [Authorize] runs in production,
giving cleaner defense-in-depth without changing the 401 vs 404 debate.

After all fixes:
- dotnet build — 0 errors 0 warnings
- dotnet test — all pass
- npm run build --prefix web — 0 TypeScript errors

git add . && git commit -m "fix(medium/low): breakpoint wiring, gates tooltip, tour copy, tooltip hover, dev filter" && git push origin develop
