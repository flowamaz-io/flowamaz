## Checkpoint
Phase: 01
Phase Title: Foundation — Infra, Auth, Org, Workspace, Help System
Total Prompts This Phase: 9
Completed: 2
Current Prompt: 03-workspace-rbac-entities
Next Prompt: 04-authentication-endpoints
Phase End Status: in progress
Session Tokens: Low
Last Updated: 2026-05-25
Notes: |
  GIT_INIT complete. Prompt 01-backend-scaffold merged to develop via PR #1
  (squash, commit 4b39330).

  Prompt 02-platform-org-entities COMPLETE — committed and pushed to develop
  (force-with-lease, this session's protocol). Platform/Organisation tier built:
  Plan/Organisation/Subscription/OrgUser/UsageAggregate entities, EF configs,
  repositories + IUnitOfWork (Core abstractions, Infra impls), OrganisationService
  (BCrypt cost 12, transactional registration + welcome email) and OrgUserService
  (credential validation + lockout). Migration AddPlatformOrganisationSchema applied;
  4 plans seeded. Build 0/0, 29 unit tests pass, Scalar 200. See results.md for the
  full DoD checklist + documented deviations (DTO placement, repo/UoW abstractions).

  KEY CONTEXT FOR PROMPT 03 (workspace + RBAC):
  - Services that touch the DB now live in Application and depend on Core repository
    interfaces + IUnitOfWork (NOT FlowAmazDbContext directly). Follow this pattern.
  - Workspace-scoped entities MUST inherit WorkspaceEntity and use
    WorkspaceRepositoryBase to enforce workspace_id isolation (CLAUDE.md rule 1).
  - ModelResolutionService.ResolveFromOrgAsync is still a stub returning null — the
    workspace→org link arrives with workspace entities in prompt 03; wire org-level
    AI config resolution then.
  - Email is unique PER ORG (composite index on org_users), not globally.

  RESUME ACTIONS (next session, after /clear):
  1. Read CLAUDE.md — execution constitution
  2. Read FUNCTIONAL.md — product spec
  3. Read this checkpoint
  4. Execute prompt 03-workspace-rbac-entities
  5. Land per session git protocol
