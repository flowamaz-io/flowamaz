---
prompt-id: 07-03-workflow-templates
phase: 07
sequence: 3
roles: [Executor, Verifier, UX, UI]
type: feature
depends-on: [07-02-audit-log]
estimated-complexity: Medium
---

# Workflow Templates — Publish and Install from Template Gallery

## Context
Library has a Templates tab (currently empty with "Soon" badge).
This prompt builds the template gallery — browse, preview, install, and publish templates.

## Objective
Complete template gallery with official templates, one-click install, and
community template publishing.

## Scope

### What to Build

**Template entities:**

WorkflowTemplate (extends ConnectorDefinition pattern):
```
workflow_templates
  id, name, slug (unique), description, category,
  is_official boolean, org_id (nullable — null for official),
  yaml_content text, preview_image_url,
  install_count int default 0, average_rating decimal,
  tags text[], version varchar,
  is_active boolean default true,
  created_at, updated_at, created_by
```

**Official templates to seed (10):**

1. Purchase Approval — 2-level approval with resubmission
2. Employee Onboarding — multi-step checklist with HR gates
3. Leave Request — manager approval with calendar integration
4. IT Support Ticket — triage → assign → resolve → close
5. Contract Review — legal review gate + e-signature placeholder
6. Expense Claim — receipt upload → manager → finance approval
7. Vendor Onboarding — procurement workflow with 3 approval levels
8. Incident Response — detect → triage → escalate → resolve → post-mortem
9. Customer Refund — CS → finance approval → payment trigger
10. Compliance Checklist — periodic review workflow with due dates

Each template has proper YAML with all node configs filled in.

**ITemplateService + TemplateService:**

`ListTemplatesAsync(category, search, page, ct)` → paginated list
`GetTemplateAsync(id, ct)` → full template with YAML
`InstallTemplateAsync(templateId, workspaceId, name, ct)` → WorkflowDefinition
  - Creates new workflow from template YAML
  - Increments install_count atomically
  - Returns workflow id for navigation to canvas

`PublishTemplateAsync(workflowId, workspaceId, templateDetails, ct)`
  - Validates workflow is published
  - Creates WorkflowTemplate from workflow YAML
  - For community: marks as pending review
  - Increments org template count

**API endpoints:**

`GET /api/v1/templates` [AllowAnonymous]
`GET /api/v1/templates/{id}` [AllowAnonymous]
`POST /api/v1/templates/{id}/install` [RequireWorkspaceRole(Designer)]
`POST /api/v1/workspaces/{id}/templates/publish` [RequireWorkspaceRole(Designer)]

**Frontend — Template Gallery:**

LibraryView.vue — Templates tab (was "Soon"):
- Category filter pills: All | Finance | HR | IT | Legal | Operations | Custom
- Search input
- Template cards: preview image + name + description + install count + [Preview] [Install]
- Sort: Most Installed | Newest | Alphabetical

`TemplateDetailView.vue` (/library/templates/{id}):
- Large preview image or YAML preview (syntax highlighted)
- Description (markdown)
- Node list: "This template uses: Trigger, 2× Human Gate, 3× Action, Router, End"
- [Install template] button → name modal → install → navigate to canvas

`InstallTemplateModal.vue`:
- "What would you like to name this workflow?" input (pre-filled from template name)
- [Install] → creates workflow, navigates to /workflows/{id}/edit
- Loading state: "Installing template..."

`PublishTemplateModal.vue` (from WorkflowDetailView):
- Template name, description, category selector
- Tags input
- Preview image upload (optional)
- [Publish template] → community submission

**Onboarding integration:**
On first workspace load (getting-started checklist not yet complete):
Show a "Start from a template" card in the dashboard creation methods section.
Clicking opens the template gallery filtered to most popular.

**Unit tests:**
- TemplateService: install increments install_count atomically
- TemplateService: install creates WorkflowDefinition from YAML
- TemplateService: publish validates workflow is published before submission
- TemplateController: unauthenticated → can list and preview templates
- TemplateController: install without Designer role → 403

## Technical Requirements
- [ ] Templates publicly browsable (no auth required)
- [ ] Install requires Designer role
- [ ] install_count atomic increment (DB-level)
- [ ] Official template YAML must pass validator before seeding
- [ ] Preview: show node type icons + count instead of full YAML

## Acceptance Criteria
- [ ] Templates tab shows 10 official templates
- [ ] Click Install → workflow created → canvas opens with template nodes
- [ ] Publish template → appears in community templates
- [ ] Search "approval" → shows approval-related templates

## Output Expected
```
backend/Flowamaz.Core/Entities/Library/WorkflowTemplate.cs
backend/Flowamaz.Application/Library/Services/TemplateService.cs
backend/Flowamaz.Api/Controllers/TemplatesController.cs
backend/Flowamaz.Infrastructure/Data/Seeds/WorkflowTemplateSeeder.cs
backend/Flowamaz.Tests.Unit/Library/TemplateServiceTests.cs
web/src/views/library/TemplateGalleryView.vue
web/src/components/library/TemplateCard.vue
web/src/components/library/InstallTemplateModal.vue
web/src/components/library/PublishTemplateModal.vue
```
