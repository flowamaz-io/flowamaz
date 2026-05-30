---
prompt-id: 08-04-multi-workspace
phase: 08
sequence: 4
roles: [Executor, Verifier, UX]
type: feature
depends-on: [08-03-documentation]
estimated-complexity: Medium
---

# Multi-Workspace Improvements — Switching, Creation, Cross-Workspace

## Context
Users can have multiple workspaces (found in testing — two workspaces created).
The workspace switcher exists but has UX issues. This prompt improves the
multi-workspace experience.

## Objective
Smooth workspace switching, create new workspace from switcher, workspace
overview page showing all workspaces.

## Scope

### What to Build

**Workspace switcher improvements:**

In web/src/components/layout/WorkspaceSwitcher.vue:

Current issues from testing:
- Shows slug as name when name is empty (fixed in earlier session)
- No way to create a new workspace from the switcher
- No search when user has many workspaces

Improvements:
1. Search input at top of dropdown (filters workspaces by name)
2. Each workspace item: name + slug + role badge (Admin/Designer/Operator/Viewer)
3. Current workspace: teal dot indicator + teal text
4. Workspace avatar: first letter of workspace name in a coloured square
   (colour derived from workspace name hash — deterministic)
5. [+ Create workspace] at bottom → opens CreateWorkspaceModal
6. Keyboard: arrow keys navigate, Enter selects, Escape closes

CreateWorkspaceModal.vue:
- Workspace name field (auto-generates slug)
- Slug field (editable, shows flowamaz.io/{slug} preview)
- [Create workspace] → POST /api/v1/workspaces → switches to new workspace

**Workspace overview page:**

Route: /workspaces (new top-level route, accessible from user menu)

WorkspaceOverviewView.vue:
Shows all workspaces the user is a member of:
Card grid, each card:
- Workspace name + slug
- Role badge
- Stats: workflow count, member count, last active
- [Open] → switches to this workspace and navigates to dashboard

[+ New workspace] button top right → CreateWorkspaceModal

**Workspace settings improvements:**

In WorkspaceSettingsView.vue:
Add "Danger zone" section at the bottom:
- [Leave workspace] — removes current user from workspace (not available if last admin)
- [Archive workspace] — Admin only, sets workspace to archived state

ArchiveWorkspaceModal.vue:
Warning: "Archiving pauses all running instances and disables new triggers.
Existing data is preserved. You can restore an archived workspace."
[Archive workspace] → PATCH /api/v1/workspaces/{id}/archive

**Backend:**

`POST /api/v1/workspaces/{id}/archive` [RequireWorkspaceRole(Admin)]
`POST /api/v1/workspaces/{id}/restore` [RequireWorkspaceRole(Admin)]
`DELETE /api/v1/workspaces/{id}/members/me` [RequireWorkspaceRole(Viewer+)]
  — leave workspace; 409 if last admin

WorkspaceDto additions:
- MemberCount (int)
- WorkflowCount (int)
- LastActiveAt (datetime?)
- Status (Active/Archived)
- UserRole (current user's role in this workspace)

**Unit tests:**
- WorkspaceService: archive sets status=Archived, pauses triggers
- WorkspaceService: leave workspace — last admin → 409
- WorkspaceSwitcher: search filters by name
- CreateWorkspaceModal: slug auto-generates from name

After fix:
dotnet build — 0 errors
npm run build --prefix web — 0 errors
git add . && git commit -m "feat(workspace): switcher search, create from switcher, overview page, archive" && git push origin develop
