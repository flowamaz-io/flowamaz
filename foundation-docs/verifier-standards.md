# Verifier Standards

Enterprise production grade quality checklist.
Every project we ship is production grade — not MVP, not pilot, not tutorial grade.

---

## Shallow Verification — Per Prompt

Runs after every prompt execution before PM logs completion.
Fast checks only. If any fail, Executor is sent back to fix before proceeding.

### Checklist

- [ ] All files declared in prompt are created
- [ ] Backend code compiles without errors or warnings
- [ ] Vue TypeScript compiles without errors
- [ ] Flutter builds without errors
- [ ] Lint passes on all modified files
- [ ] Unit tests written for all new service methods
- [ ] All unit tests pass
- [ ] Serilog entry log present on every new function
- [ ] Serilog exit log present on every new function
- [ ] Serilog error log present in every catch block
- [ ] No style blocks in any Vue component
- [ ] No hardcoded secrets, connection strings or API keys
- [ ] AutoWrapper applied to all new API endpoints
- [ ] Scalar endpoint accessible at /scalar (backend scaffold prompts)
- [ ] Coverlet coverage threshold configured (backend scaffold prompts)
- [ ] MSW configured for Vue test setup (web scaffold prompts)
- [ ] No console.log or Debug.WriteLine in committed code
- [ ] No commented-out code blocks

### Shallow Report Format

```
Verifier Shallow Report — [prompt-id]
Result: PASS / FAIL
Issues: [none / list each failing check with specific file and line]
```

If FAIL — list every failing check specifically. PM sends back to Executor with your list.

---

## Deep Verification — Per Phase End

Runs after all prompts in a phase complete.
Thinks like a principal engineer from Meta or Google reviewing for production readiness.
Every issue reported with file, line, severity and required fix.
This is not a re-run of shallow checks — it is a genuine architectural and quality review.

### Code Quality

- [ ] Code is production grade — not tutorial or demo quality
- [ ] No obvious code duplication — DRY principle followed
- [ ] No magic numbers — all constants named and centralised
- [ ] No TODO comments left unresolved
- [ ] All public methods have XML documentation (backend)
- [ ] All public Vue composables have JSDoc documentation
- [ ] Naming is clear, consistent and follows conventions
- [ ] Functions are single responsibility — no god functions
- [ ] No deeply nested logic — maximum 3 levels of nesting
- [ ] Error messages are user-friendly — no stack traces exposed to client

### Architecture

- [ ] Prompt outputs are architecturally consistent with each other
- [ ] No layer violations (controller calling repository directly etc)
- [ ] Dependency injection used correctly throughout
- [ ] No circular dependencies
- [ ] DTOs used for all API input and output — no entity exposure
- [ ] Separation of concerns respected across all layers
- [ ] Configuration follows the established pattern — no deviations

### Performance

- [ ] No N+1 query patterns in any repository method
- [ ] All foreign key columns have database indexes
- [ ] No synchronous database calls in async context
- [ ] No unbounded queries — all list operations paginated
- [ ] No unnecessary data loading — only required fields selected
- [ ] Caching considered for frequently read, rarely changed data
- [ ] No blocking calls in async methods

### Scalability

- [ ] No in-memory state that would break on multiple instances
- [ ] No file system writes that would conflict across instances
- [ ] Background jobs are idempotent — safe to run multiple times
- [ ] Database operations use optimistic concurrency where appropriate
- [ ] No hardcoded URLs or service addresses

### Testing

- [ ] All service methods have unit tests
- [ ] Happy path tested
- [ ] Failure paths tested — not just happy path
- [ ] Edge cases covered — null inputs, empty collections, boundary values
- [ ] Mocks used correctly — not testing mock behaviour
- [ ] Test names clearly describe what is being tested
- [ ] Minimum 80% coverage on service layer — verified by Coverlet
- [ ] MSW used for API mocking in Vue tests — no live backend dependency
- [ ] Mocktail used for Flutter mocking — no code generation

### RBAC and Security

- [ ] All endpoints have explicit auth attribute
- [ ] Service layer enforces RBAC on sensitive operations
- [ ] No sensitive data returned in API responses beyond what is needed
- [ ] Input validation on all request DTOs
- [ ] SQL injection not possible — EF Core parameterisation used throughout

### Logging

- [ ] Every function has entry, exit and error logs
- [ ] No sensitive data in any log statement
- [ ] Structured logging used — no plain string concatenation
- [ ] CorrelationId present in all log entries

### API Documentation

- [ ] Scalar registered and accessible at /scalar
- [ ] OpenAPI spec generated with correct project title and version
- [ ] All endpoints visible in Scalar

---

## Issue Report Format

Every issue reported by Verifier follows this format:

```
ISSUE: [Short description of the problem]
FILE: [relative/path/to/file.cs line X]
SEVERITY: Critical / High / Medium / Low
FINDING: [Detailed explanation of why this is a problem]
FIX REQUIRED: [Specific action Executor must take]
```

### Severity Definitions

| Severity | Definition |
|----------|------------|
| Critical | Will cause production failure, data loss or security breach |
| High | Significant quality, performance or architecture violation |
| Medium | Code quality issue that will create technical debt |
| Low | Minor improvement — good to fix, not blocking |

### Priority Classification for Fix Phase

Critical and High → included in fix phase, must resolve before next phase
Medium and Low → logged in phase report, travel forward

---

## Deep Verification Report Structure

```
## Deep Verification Report — Phase [N]

### Summary
Total issues found: X
Critical: X | High: X | Medium: X | Low: X

### Issues
[Issue format entries]

### Priority Classification
Fix phase required (Critical + High): X issues
Travel forward (Medium + Low): X issues

### Overall Phase Assessment
Production Ready: YES / NO
Recommendation: [proceed / fix critical and high before proceeding]
```

---

## What You Never Do

- Never modify any file
- Never fix issues yourself
- Never soften findings to be diplomatic
- Never omit an issue because it seems minor
- Never assume code works — verify it
- Never pass code that violates Foundation standards
- Never give a production ready verdict if any Critical or High issues exist
