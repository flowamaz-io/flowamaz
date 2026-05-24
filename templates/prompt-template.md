# Prompt File Format

Use this template for both regular phase prompts and fix phase prompts.
The front matter roles declaration controls which agents PM invokes.

---

## Regular Phase Prompt

```
---
prompt-id: [NN]-[short-name]
phase: [phase number]
sequence: [sequence number within phase]
roles: [Executor, Verifier]
type: feature
depends-on: []
estimated-complexity: Low / Medium / High
---

# [Prompt Title]

## Context
[Where this fits in the project. What has been built so far that is relevant.]

## Objective
[Single clear sentence. One objective only. Split if multiple.]

## Scope

### What to Build
[Precise list of files to create or modify. File paths, class names, method names.]

### What NOT to Do
[Explicit boundaries. Prevents Executor from over-building.]

## Technical Requirements

### Backend (if applicable)
- [ ] Requirement

### Web (if applicable)
- [ ] Requirement

### Mobile (if applicable)
- [ ] Requirement

### Database (if applicable)
- [ ] Requirement

## Acceptance Criteria
[Objectively verifiable. Pass or fail, no ambiguity.]

- [ ] Criterion 1
- [ ] Criterion 2

## Output Expected
[Every file that must exist when complete. Verifier checks this list.]

path/to/file1.cs
path/to/file2.vue

## Notes for Executor
[Additional guidance, gotchas, decisions already made.]
```

---

## Fix Phase Prompt

```
---
prompt-id: fix-[NN]-[short-name]
phase: fix-phase-[N]
sequence: [sequence number within fix phase]
roles: [Executor, Verifier]
type: fix
source-issue: [Verifier / Security / Testing / UX / UI]
severity: Critical / High
depends-on: []
---

# Fix: [Short Description of Issue Being Fixed]

## Issue Being Fixed
[Copy the exact issue from the phase report]

Original finding:
ISSUE: [from phase report]
FILE: [from phase report]
SEVERITY: [from phase report]
FINDING: [from phase report]
FIX REQUIRED: [from phase report]

## Objective
[Single clear sentence describing what the fix achieves.]

## Scope

### What to Change
[Precise list of files to modify. Be specific — file paths, method names.]

### What NOT to Change
[Explicit boundaries. Do not introduce new features during a fix phase.]

## Technical Requirements
- [ ] Fix the specific issue described above
- [ ] Do not break existing functionality
- [ ] Update or add unit tests to cover the fixed behaviour
- [ ] All existing tests must continue to pass

## Acceptance Criteria
- [ ] Original issue no longer present
- [ ] Unit tests cover the fixed behaviour
- [ ] All existing tests pass
- [ ] No new issues introduced

## Output Expected
[Files that will be modified]

path/to/fixed-file.cs

## Notes for Executor
[Any context needed to implement the fix correctly.]
```

---

## Roles Reference

| Role | When to Include |
|------|----------------|
| Executor | Always — every prompt |
| Verifier | Always — every prompt |
| UX | Prompts involving user flows, navigation, information architecture |
| UI | Prompts involving components, screens, visual layout |
| Security | Prompts involving auth, file upload, external API calls |
| Testing | Phase end prompts only — not individual feature prompts |

## Example Role Declarations

```
roles: [Executor, Verifier]
roles: [Executor, Verifier, UX, UI]
roles: [Executor, Verifier, Security]
```
