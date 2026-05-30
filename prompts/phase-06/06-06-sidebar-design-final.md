---
prompt-id: 06-06-sidebar-design-final
phase: 06
sequence: 6
roles: [Executor, Verifier, UI, UX]
type: feature
depends-on: [06-05-connector-marketplace]
estimated-complexity: Medium
---

# Final App Shell Polish — Navigation, Help Panel, Notifications

## Context
Phase 5 delivered the dark sidebar. This prompt completes the app shell:
adds Phase 6 navigation items, implements the notification centre, improves
the help panel, and fixes remaining UX issues found in testing.

## Objective
Complete app shell with all navigation items, notification centre, help panel
improvements, and polish from testing session findings.

## Scope

### What to Build

**Add Phase 6 navigation items to sidebar:**

Under BUILD section:
- Connectors (existing) → add sub-item: Health, Webhooks
- Library (existing) → add sub-item: Submit

Under SETTINGS section:
- Workspace (existing)
- Members (existing)
- API Keys (existing)
- Webhooks (new) → /settings/webhooks
- SSO (new, shown only to Org Admins) → /settings/sso

**Notification centre:**

Replace the help (?) icon in the top bar with a bell icon.
Notifications are in-app alerts for:
- Workflow instance completed/failed
- Human gate pending (you are an approver)
- Human gate decided (you submitted a request)
- Team member invited (you are an admin)
- Trial expiring in 7 days

`INotificationService + NotificationService`:

Notification entity:
```
notifications
  id, user_id, org_id, workspace_id,
  type (instance_complete|gate_pending|gate_decided|member_invited|trial_expiring),
  title, message, action_url,
  is_read boolean default false,
  created_at
```

`GET /api/v1/notifications?unread=true&limit=20`
`POST /api/v1/notifications/{id}/read`
`POST /api/v1/notifications/read-all`

Bell icon in top bar:
- Shows unread count badge (red, max "9+")
- Click → dropdown panel (max-height 480px, scrollable)
- Each notification: icon + title + relative time + [mark read]
- [Mark all read] button at top
- [View all] link at bottom
- Empty state: "You're all caught up"
- Auto-polls every 60 seconds (not WebSocket — keep it simple)

Fire notifications from:
- WorkflowOrchestrator: on instance terminal state → notify workflow creator
- HumanGateNodeWorker: on gate delivery → notify assignee
- GateService: on gate decision → notify requestor
- MemberService: on invite → notify admin

**Help panel improvements:**

The help panel (Shift+?) currently shows articles.
Improve it:

1. Add search that works (currently placeholder):
   - Search input filters articles by title and content
   - Debounced 300ms
   - Shows "No results" with suggestion to contact support

2. Add "Contact support" button at bottom:
   - Opens email to support@flowamaz.io with pre-filled subject
   - Subject: "Flowamaz Support — {current page} — {org name}"

3. Add keyboard shortcut to close: Escape

4. Add article feedback:
   - Each article: "Was this helpful? 👍 👎"
   - Click records feedback (POST /api/v1/help/feedback)
   - Shows "Thanks for your feedback!"

**Fix remaining UX issues from testing:**

1. Canvas title shows "Untitled Workflow" — fix to show actual workflow name
   In SfgCanvas.vue: load workflowName from GET /workflows/{id} response

2. Dashboard subtitle "Here's what's happening in ." — trailing period with no name
   In DashboardView.vue: use workspace.name ?? workspace.slug, trim the period

3. Getting started checklist progress not persisting
   Store completion state server-side in user_preferences table
   Key: "checklist_{workspaceId}", value: { completedItems: string[] }

**Unit tests:**
- NotificationService: instance complete → notification created for creator
- NotificationService: gate pending → notification created for assignee
- NotificationService: mark all read → all user notifications marked
- HelpPanel: search filters articles correctly
- HelpPanel: feedback submitted → POST to /api/v1/help/feedback

## Technical Requirements
- [ ] Notifications: auto-poll every 60s (not WebSocket)
- [ ] Bell badge: shows correct unread count
- [ ] Help panel: search debounced 300ms
- [ ] Canvas title: actual workflow name from API
- [ ] Dashboard subtitle: no trailing period

## Acceptance Criteria
- [ ] Bell shows "3" badge when 3 unread notifications
- [ ] Click bell → dropdown shows notifications
- [ ] Mark all read → badge disappears
- [ ] Help panel search: type "connector" → shows connector articles
- [ ] Canvas opens with correct workflow name in title bar

## Output Expected
```
backend/Flowamaz.Core/Entities/Notifications/Notification.cs
backend/Flowamaz.Application/Notifications/NotificationService.cs
backend/Flowamaz.Api/Controllers/NotificationsController.cs
backend/Flowamaz.Tests.Unit/Notifications/NotificationServiceTests.cs
web/src/components/layout/NotificationBell.vue
web/src/components/layout/NotificationDropdown.vue
web/src/components/layout/HelpPanel.vue (updated)
web/src/views/dashboard/DashboardView.vue (subtitle fix)
web/src/components/canvas/SfgCanvas.vue (title fix)
```
