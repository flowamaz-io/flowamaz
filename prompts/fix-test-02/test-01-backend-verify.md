Read CLAUDE.md, FUNCTIONAL.md, checkpoint.md, phase-08-report.md.

GOAL: Establish a trustworthy local baseline before any E2E work. Do NOT change product code
in this prompt unless a build/test is red — then fix only that and record it.

1. Dependencies
   docker compose -f infrastructure/docker-compose.dev.yml up -d
   Confirm Postgres and Redis are healthy (docker compose ps).

2. Backend
   cd backend
   dotnet --version                       # must satisfy global.json (10.0.101, latestMinor)
   dotnet build Flowamaz.sln -warnaserror # expect 0 errors / 0 warnings
   dotnet test Flowamaz.Tests.Unit        # phase-08 claims 553/553
   dotnet test Flowamaz.Tests.Integration # phase-08 claims 116 pass + 1 skip (needs Docker)

3. Migrations on a FRESH database
   Drop and recreate the dev DB, then:
   dotnet ef database update --project Flowamaz.Infrastructure --startup-project Flowamaz.Api
   Confirm every migration applies and the template seeder runs. Record the migration count.

4. Run the API and smoke it
   dotnet run --project Flowamaz.Api
   curl http://localhost:5000/health        → 200
   open http://localhost:5000/scalar        → loads
   Register a user + org via the API, log in, create a workspace, list workspaces.

5. Other packages (all must be green)
   npm ci --prefix web && npm run build --prefix web && npm run test --prefix web -- --run
   npm ci --prefix marketing && npm run build --prefix marketing && npm run test --prefix marketing -- --run
   npm ci --prefix cli && npm test --prefix cli
   npm ci --prefix vscode-extension && npm test --prefix vscode-extension

6. AI stub check
   AiCompletionService silently returns stub output when UseStubCompletion=true OR no API key.
   Record in results.md which mode the local run used. Add a single startup log line at
   Warning level when stub mode is active ("AI STUB MODE — responses are canned") so this
   can never be mistaken for real AI output in a demo. Unit test for the warning.

DELIVERABLE: append "fix-test-02-01 Baseline" to results.md with an exact table:
package | command | expected (phase-08) | actual | delta. Any delta is a finding.
Update checkpoint.md → Current Prompt: test-02-e2e-harness.
Commit: test(baseline): local verification baseline [fix-test-02-01]
