---
prompt-id: 07-05-onboarding-improvements
phase: 07
sequence: 5
roles: [Executor, Verifier, UX, UI]
type: feature
depends-on: [07-04-advanced-monitoring]
estimated-complexity: Medium
---

# Onboarding Improvements — Product Tour, Contextual Help, Empty States

## Context
Registration and onboarding wizard exist (Phase 1) but the first-run experience
needs polish — an interactive product tour, better empty states on every page,
and contextual help that guides new users to their first workflow.

## Objective
Interactive product tour on first login, improved empty states with actionable CTAs,
and contextual onboarding tips throughout the app.

## Scope

### What to Build

**Product tour (first login only):**

Show automatically after onboarding wizard completes for the first time.
Store in user_preferences: "tour_completed_{workspaceId}" = true
If already true: never show again.

Tour steps (using a simple custom overlay component, no external library):

Step 1 — Dashboard (spotlight sidebar):
"This is your workspace sidebar. Navigate between workflows, instances, and settings."
[Skip tour] [Next →]

Step 2 — Create workflow (spotlight creation methods):
"Create your first workflow in seconds. Describe what you want to automate in plain English."
[← Back] [Try it now] (clicks Plain English) / [Next →]

Step 3 — Library (spotlight library nav item):
"The Library has 13 official connectors — Slack, GitHub, PostgreSQL, and more."
[← Back] [Next →]

Step 4 — Canvas (shown after first workflow created):
"This is the visual canvas. Drag nodes, connect them, and use Co-pilot to make changes."
[← Back] [Next →]

Step 5 — Done:
"You're ready to automate. Here's a template to get you started."
Shows the Purchase Approval template card.
[Start from template] [Explore on my own]

Tour component: `ProductTour.vue`
- Dark overlay with cutout spotlight around the highlighted element
- Tooltip positioned relative to spotlight (above/below/left/right)
- Progress dots at bottom: ●●○○○
- Keyboard: Escape = skip, Arrow Right = next

**Improved empty states:**

Every page that can be empty needs a designed empty state — not just "No items yet."

`WorkflowListView.vue` empty state:
- Illustration: workflow graph outline (SVG, teal accent)
- Title: "No workflows yet"
- Description: "Create your first automation in seconds"
- 3 quick-start cards: Plain English | Start from template | Draw on canvas

`InstanceListView.vue` empty state:
- Title: "No runs yet"
- Description: "Trigger a published workflow to see runs appear here in real time."
- CTA: [Go to Workflows] → /workflows

`GatesView.vue` empty state:
- Title: "No pending approvals"
- Description: "When a workflow reaches a human gate, it will appear here for review."
- ✓ All caught up

`LibraryView.vue` (installed connectors, empty):
- Title: "No connectors installed"
- Description: "Connect Slack, your database, or any API to power your workflows."
- CTA: [Browse connectors]

`AuditLogView.vue` empty state:
- Title: "No activity yet"
- Description: "Workspace actions will appear here as they happen."

**Contextual help tooltips:**

Add `?` icon tooltips on complex UI elements that open a short explanation:

- Workflow status badges (Draft/Published): "Draft workflows can be tested but not triggered in production."
- Health score: "Health score reflects workflow quality — validation, coverage, and best practices."
- Empathy score: "Empathy score measures how human-friendly this workflow is for the people in it."
- Gates badge in sidebar: "Workflows waiting for your approval."
- Co-pilot button: "Ask Co-pilot to modify your workflow in plain English. Ctrl+K"
- API keys page: "Use API keys to trigger workflows from external systems."

Implementation: `FmTooltip.vue` component (already likely exists — check and reuse).
Position: above or below based on available space.
Trigger: hover or click on `?` icon.

**Welcome email update:**

Update EmailTemplateService.Welcome to include:
- Link to the product tour: "Take a 2-minute tour →"
- Link to the template gallery: "Start from a template →"
- Link to docs: "Read the docs →"

**Unit tests:**
- ProductTour: shows on first login, not shown again after completion
- ProductTour: Escape key skips tour
- ProductTour: preference stored in user_preferences
- EmptyState: WorkflowList shows creation CTAs when workflow count = 0

## Technical Requirements
- [ ] Tour: custom overlay, no external tour library
- [ ] Tour: never shows after completion (user_preferences check)
- [ ] Tour: Escape to skip, works with keyboard only
- [ ] All 6 empty states designed with actionable CTAs
- [ ] Contextual tooltips on all listed complex elements

## Acceptance Criteria
- [ ] First login → tour starts automatically after onboarding
- [ ] Complete tour → never shows again (even after page refresh)
- [ ] Workflows page empty → shows 3 creation method cards
- [ ] Hover ? on Health score → tooltip explains what it means
- [ ] Welcome email includes tour link

## Output Expected
```
web/src/components/onboarding/ProductTour.vue
web/src/components/onboarding/TourStep.vue
web/src/components/shared/FmEmptyState.vue (updated)
web/src/components/shared/FmTooltip.vue
web/src/views/workflow/WorkflowListView.vue (empty state)
web/src/views/instance/InstanceListView.vue (empty state)
web/src/views/gates/GatesView.vue (empty state)
web/src/tests/ProductTour.test.ts
```
