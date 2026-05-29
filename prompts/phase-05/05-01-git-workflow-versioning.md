---
prompt-id: 05-01-git-workflow-versioning
phase: 05
sequence: 1
roles: [Executor, Verifier, Security]
type: feature
depends-on: []
estimated-complexity: High
---

# Git Versioning — Workflow YAML as Git Repository

## Context
Phase 4 complete. Phase 5 delivers the developer-facing layer: Git versioning,
CLI, and developer tools. This prompt implements the Git-native versioning model
where each workspace's workflows live in a bare Git repository.
See FUNCTIONAL.md §2.6 (Git-native versioning).

## Objective
Each workspace gets a bare Git repo. Workflow YAML is committed on every publish.
Instances are pinned to a commit SHA forever. Diff view between versions.
Branch = draft, tag = release, commit SHA = immutable instance reference.

## Scope

### What to Build

**WorkspaceGitService (Flowamaz.Infrastructure/Git/):**

Uses LibGit2Sharp. Each workspace gets:
`{GIT_REPOS_BASE_PATH}/{workspaceId}/workflows.git` — bare repository

`IWorkspaceGitService`:

`InitialiseRepoAsync(workspaceId, ct)`:
- Creates bare git repo at the workspace path
- Sets default branch to `main`
- Creates initial commit: "chore: initialise workflow repository"
- Called automatically when workspace is created

`CommitWorkflowAsync(workspaceId, workflowId, yamlContent, message, authorName, authorEmail, ct)`
  → CommitResult { CommitSha, BranchName, TagName? }
- Writes `{workflowId}.yaml` to the repo
- Creates commit with author info and message
- Returns commit SHA

`PublishWorkflowAsync(workspaceId, workflowId, version, ct)` → string tagName
- Creates a lightweight tag: `{workflowId}/v{version}` pointing to HEAD
- Used when workflow is published

`GetWorkflowAtCommitAsync(workspaceId, workflowId, commitSha, ct)` → string yamlContent
- Reads `{workflowId}.yaml` from the specific commit
- Used to reconstruct historical YAML for an instance

`GetDiffAsync(workspaceId, workflowId, fromSha, toSha, ct)` → WorkflowDiff
- Returns unified diff between two commits for this workflow file
- Parsed into structured changes: { added[], removed[], changed[] } per node

`GetHistoryAsync(workspaceId, workflowId, limit, ct)` → List<WorkflowCommit>
- Returns commit history for this workflow file
- WorkflowCommit: { Sha, ShortSha, Message, AuthorName, AuthorEmail, CommittedAt }

`CreateBranchAsync(workspaceId, branchName, fromSha?, ct)`
`CheckoutBranchAsync(workspaceId, branchName, ct)`
`MergeBranchAsync(workspaceId, sourceBranch, targetBranch, ct)` → MergeResult

**Integration with WorkflowDefinitionService:**

When workflow YAML is saved (create or update):
- Call WorkspaceGitService.CommitWorkflowAsync
- Store returned CommitSha in WorkflowDefinition.CurrentVersion

When workflow is published:
- Call PublishWorkflowAsync (creates tag)
- Create WorkflowVersion record with CommitSha from git

When instance is triggered:
- Pin WorkflowVersionId at trigger time (already done in Phase 2)
- The pinned version's CommitSha is the immutable reference for this instance forever
- GetWorkflowAtCommitAsync reconstructs exact YAML for any historical instance

**API endpoints:**

`GET /api/v1/workspaces/{workspaceId}/workflows/{id}/history`
[RequireWorkspaceRole(Viewer)]
Returns: List<WorkflowCommit> (paginated, last 50)

`GET /api/v1/workspaces/{workspaceId}/workflows/{id}/diff?from={sha}&to={sha}`
[RequireWorkspaceRole(Designer)]
Returns: WorkflowDiff { fromSha, toSha, changes: { added[], removed[], modified[] } }

`GET /api/v1/workspaces/{workspaceId}/workflows/{id}/at/{commitSha}`
[RequireWorkspaceRole(Viewer)]
Returns: WorkflowDefinitionDto with yamlContent at that commit

**Frontend — Version History + Diff View:**

In WorkflowDetailView (Versions tab — already exists):
- Fetch GET /history → list of commits with SHA, message, author, date
- Each row: short SHA badge (7 chars), message, author avatar initial, relative time
- [View] → opens YAML at that version (read-only CodeMirror)
- [Diff] → opens DiffView modal

`FmVersionDiffView.vue` (modal):
- Side-by-side diff: left = from, right = to
- Added lines: green background
- Removed lines: red background
- Changed lines: amber background
- Node-level summary above: "2 nodes added, 1 node removed, 3 nodes modified"

**Workspace repo initialisation:**
In WorkspaceService.CreateWorkspaceAsync:
After creating workspace, call IWorkspaceGitService.InitialiseRepoAsync.
If GIT_REPOS_BASE_PATH directory does not exist: create it.

**Migration note:**
Existing workspaces have no git repo. Add a Quartz.NET one-time migration job:
`WorkspaceGitInitialisationJob` — runs once on startup, initialises repo for any
workspace without a git repo, commits current YAML for all their workflows.

**Unit tests:**
- WorkspaceGitService: InitialiseRepo creates bare repo
- CommitWorkflow: file exists in repo at returned SHA
- GetWorkflowAtCommit: returns correct YAML for historical commit
- GetDiff: detects added, removed, modified nodes between two commits
- PublishWorkflow: tag created with correct name

## Technical Requirements
- [ ] LibGit2Sharp — only dependency for git operations
- [ ] Bare repos only — never clone, never working directory outside of operations
- [ ] CommitSha stored in WorkflowDefinition.CurrentVersion on every save
- [ ] GetWorkflowAtCommitAsync used when loading historical YAML for instances
- [ ] GIT_REPOS_BASE_PATH: directory created if not exists, fail startup if path not writable
- [ ] Existing workspaces: one-time initialisation job on startup

## Acceptance Criteria
- [ ] Create workflow → edit → publish → history shows 2 commits
- [ ] Diff between two commits → correct added/removed/modified nodes
- [ ] Instance triggered at v1 → YAML reconstructed from v1 commit SHA (not current)
- [ ] FmVersionDiffView: colour-coded line diff renders correctly

## Output Expected
```
backend/Flowamaz.Core/Interfaces/Git/IWorkspaceGitService.cs
backend/Flowamaz.Infrastructure/Git/WorkspaceGitService.cs
backend/Flowamaz.Infrastructure/Jobs/WorkspaceGitInitialisationJob.cs
backend/Flowamaz.Api/Controllers/WorkflowVersionsController.cs
backend/Flowamaz.Tests.Unit/Git/WorkspaceGitServiceTests.cs
backend/Flowamaz.Tests.Integration/Git/GitVersioningTests.cs
web/src/components/workflow/FmVersionDiffView.vue
web/src/views/workflow/WorkflowDetailView.vue (versions tab updated)
```
