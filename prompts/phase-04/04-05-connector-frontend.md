---
prompt-id: 04-05-connector-frontend
phase: 04
sequence: 5
roles: [Executor, Verifier, UI, UX]
type: feature
depends-on: [04-04-human-gate-node]
estimated-complexity: Medium
---

# Connector Library UI + Credential Management + Health Dashboard

## Context
All connector backend exists. Now build the Library section UI (Connectors + Templates tabs),
credential management views, and the connector health dashboard.

## Objective
Library section with Connectors tab (Phase 4 scope), credential setup wizard,
connector health dashboard. Templates tab scaffolded (populated in Phase 6).

## Scope

### What to Build

**Library Section (/library) — Connectors Tab:**

New sidebar item: "Library" with book icon (Lucide BookOpen).

`LibraryView.vue` with tabs:
- 🔌 Connectors (this prompt)
- 📋 Templates (scaffold only — Phase 6)
- 📝 Forms (coming soon badge)
- 📦 Packs (coming soon badge)

Connectors tab:
- Search input: "Search connectors..."
- Filter pills: All | Finance | HR | Engineering | Database | Messaging | DevOps | AI
- Sort: Most installed | Newest | A-Z
- Grid of ConnectorCard components (3 per row on desktop, 1 on mobile)
- Empty search state: "No connectors found for '{query}' — Contribute one →"

`ConnectorCard.vue`:
- Connector icon (emoji or SVG from manifest)
- Display name, description (truncated 80 chars)
- Category badge, tier badge (Official/Community/Verified)
- Install count (mock data for Phase 4 — real installs in Phase 6 Library sync)
- Rating stars (mock 4.5 for official — real ratings Phase 6)
- [Install] button → if not installed, if installed → [✓ Installed] (disabled)
- Click card → ConnectorDetailView

`ConnectorDetailView (/library/connectors/:id)`:
- Full description, all operations listed with input/output schemas
- Auth type explained
- Setup instructions
- [Install & Connect] button → opens CredentialSetupWizard

**Credential Setup Wizard:**

`CredentialSetupWizard.vue` — modal, 3 steps:

Step 1 — Choose auth method:
Show auth type from connector manifest.
If oauth2: "Connect with OAuth" → big button. Auto-advances.
If api-key: show API key input field.
If basic: show username + password fields.

Step 2 — OAuth flow (oauth2 connectors):
Call POST /connectors/{id}/oauth/initiate → open popup.
Show "Connecting to {connector}..." spinner.
Listen for PostMessage from popup → on success: advance to step 3.
On failure: show error + retry button.

Step 3 — Credential saved:
"✓ Connected to {connector}!" with connector icon.
Optional: name the credential (default: "{ConnectorName} workspace").
[Done] → closes wizard, connector card updates to "✓ Installed".

**Connector Health Dashboard (/library/health):**

`ConnectorHealthView.vue`:
Table showing all installed connectors with health status:
- Connector name + icon
- Credential name
- Status badge: 🟢 Healthy | 🟡 Rate Limited | 🔴 Expired | ⚠️ Error
- Last used timestamp
- Calls last hour
- Expires at (for OAuth tokens)
- [Reconnect] button if expired/error

Auto-refreshes every 30 seconds.

**Canvas Node Inspector — connector picker:**
In SfgCanvas NodeInspector (Action node):
- "Connector" dropdown: shows installed workspace connectors
- On connector select: "Operation" dropdown populates from connector manifest operations
- On operation select: input fields render from operation.input_schema

This wires the canvas directly to installed connectors.

**Help articles:**
Add to web/src/help/articles/connectors/:
- `add-your-first-connector.md` — complete setup guide
- `oauth-credential-setup.md` — OAuth wizard walkthrough
- `connector-health-dashboard.md` — interpreting health statuses

Update articleMap.ts:
- /library → 'connectors/add-your-first-connector'
- /library/health → 'connectors/connector-health-dashboard'

## Technical Requirements
- [ ] Zero style blocks
- [ ] OAuth wizard: opens popup, receives PostMessage, handles failure
- [ ] Credential Setup Wizard: 3 steps, animated transitions
- [ ] ConnectorCard: Install button correctly shows Installed state
- [ ] Node inspector: connector + operation pickers dynamically populated
- [ ] Health dashboard: auto-refreshes every 30s

## Acceptance Criteria
- [ ] /library → ConnectorCard grid renders for all 13 official connectors
- [ ] Click "Install & Connect" → CredentialSetupWizard opens
- [ ] OAuth step: popup opens, PostMessage received → step 3 shown
- [ ] Health dashboard: expired credential shows 🔴 Expired + Reconnect button
- [ ] Action node inspector: connector dropdown → operation dropdown works

## Output Expected
```
web/src/views/library/LibraryView.vue
web/src/views/library/ConnectorDetailView.vue
web/src/views/library/ConnectorHealthView.vue
web/src/components/library/ConnectorCard.vue
web/src/components/library/CredentialSetupWizard.vue
web/src/components/editor/NodeInspectorConnectorPicker.vue
web/src/help/articles/connectors/ (3 articles)
web/src/services/connector.service.ts
```
