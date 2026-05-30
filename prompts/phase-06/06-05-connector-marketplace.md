---
prompt-id: 06-05-connector-marketplace
phase: 06
sequence: 5
roles: [Executor, Verifier, UX, UI]
type: feature
depends-on: [06-04-sso]
estimated-complexity: Medium
---

# Connector Marketplace — Real Ratings, Install Counts, Community Submissions

## Context
The Library shows 13 official connectors with mock data (static 4.5★, fake install counts).
This prompt replaces mock data with real metrics, adds community connector submissions,
and builds the full marketplace experience.

## Objective
Real install counts, real ratings, connector submission flow for community connectors,
and a connector detail page.

## Scope

### What to Build

**Real install metrics:**

ConnectorDefinition entity additions:
- InstallCount (int, updated on install/uninstall)
- AverageRating (decimal, computed from ConnectorRatings)
- RatingCount (int)
- LastUpdatedAt (datetime)
- IsOfficial (bool — true for the 13 seeded connectors)

ConnectorRating entity:
```
connector_ratings
  id               uuid pk
  connector_id     uuid fk connector_definitions
  org_id           uuid fk organisations
  rating           int (1-5)
  review           text (optional, max 500 chars)
  created_at
  unique(connector_id, org_id) — one rating per org per connector
```

Update install/uninstall to increment/decrement InstallCount atomically.

**Connector detail page:**

Route: /library/connectors/{connectorId}

`ConnectorDetailView.vue`:
- Header: connector icon + name + category badge + Official/Community badge
- Install count + average rating (star display)
- Description (markdown rendered)
- Actions: Install / Uninstall / Update (if newer version)
- Tabs:
  - Overview (description + use cases)
  - Actions (list of available actions with input/output schemas)
  - Changelog (version history)
  - Reviews (rating + review list, leave a review if installed)

**Rating and review:**

`RateConnectorModal.vue`:
- 1-5 star selector
- Optional review text (max 500 chars)
- Submit only available if connector is installed by the org

**Community connector submission:**

Route: /library/submit

`ConnectorSubmissionView.vue`:
Wizard:
Step 1 — Connector info: name, description, category, icon (upload)
Step 2 — Manifest: upload connector manifest YAML (validated client-side)
Step 3 — Actions: list of actions defined in manifest (read-only preview)
Step 4 — Submit: creates a GitHub PR to flowamaz-io/connectors repo

Backend for community submission:
`POST /api/v1/library/connectors/submit`
[RequireWorkspaceRole(Designer)]
- Validate manifest YAML
- Create GitHub PR via GitHub App API to flowamaz-io/connectors
- Return PR URL
- Store submission record (pending review status)

ConnectorSubmission entity:
```
connector_submissions
  id, org_id, connector_name, manifest_yaml, github_pr_url,
  status (pending|approved|rejected), reviewer_notes, created_at
```

**Library UI updates:**

ConnectorCard.vue — replace mock data:
- Show real install count: "1.2k installs" (formatted)
- Show real rating: "4.3 ★ (47)"
- Show "Community" badge for non-official connectors

LibraryView.vue tabs:
- Connectors (existing — now with real data)
- Templates (existing)
- Submit (new — links to /library/submit)

Filter additions:
- All | Official | Community
- Category filter (existing)
- Sort: Most Installed | Highest Rated | Recently Updated

**Unit tests:**
- ConnectorService: install increments InstallCount atomically
- ConnectorService: rate connector stores rating, updates average
- ConnectorService: duplicate rating from same org → updates existing
- ConnectorSubmissionService: valid manifest → creates GitHub PR

## Technical Requirements
- [ ] InstallCount updated atomically (database-level increment)
- [ ] Average rating recomputed on each rating submission
- [ ] One rating per org per connector (upsert behaviour)
- [ ] Connector detail page accessible without being logged in (public)
- [ ] GitHub PR creation uses GitHub App credentials

## Acceptance Criteria
- [ ] Install a connector → install count increments by 1
- [ ] Rate a connector → average rating updates
- [ ] Connector detail page shows real metrics
- [ ] Submit connector → GitHub PR created
- [ ] Library filters work with real data

## Output Expected
```
backend/Flowamaz.Core/Entities/Library/ConnectorRating.cs
backend/Flowamaz.Core/Entities/Library/ConnectorSubmission.cs
backend/Flowamaz.Application/Library/Services/ConnectorMarketplaceService.cs
backend/Flowamaz.Api/Controllers/ConnectorMarketplaceController.cs
backend/Flowamaz.Tests.Unit/Library/ConnectorMarketplaceServiceTests.cs
web/src/views/library/ConnectorDetailView.vue
web/src/views/library/ConnectorSubmissionView.vue
web/src/components/library/ConnectorCard.vue (updated)
web/src/components/library/RateConnectorModal.vue
```
