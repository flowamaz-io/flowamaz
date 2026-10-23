# Chrome end-to-end test (Claude Code + Claude in Chrome)

A guided, human-like test of Flowamaz in your real Chrome browser: 19 journeys covering sign-up,
onboarding, workspaces, templates, the canvas, approvals, webhooks, credentials, publishing and
settings. It ends by **building the Loan Approval workflow in the UI (J17)** and driving it through
the public API with a mock loan application, without any UI (J18). **Stripe payments are skipped.** The
paid-template lock is still checked without paying.

## What you need
- Docker, the .NET 10 SDK and Node 22 on this machine (the same as for development). Port 4010 must be free
  for the mock loan application in J18.
- Chrome with the **Claude in Chrome** extension installed and signed in.
- Claude Code.
- *Optional:* an Anthropic API key. Without one, the AI creation journeys (J11–J13) are recorded as
  BLOCKED; everything else runs normally.

## Run it

1. **Start the app** from the repo root:
   ```bash
   # macOS / Linux / WSL / Git Bash
   export ANTHROPIC_KEY=sk-ant-...        # optional
   ./prompts/e2e-chrome/start-local.sh
   ```
   ```powershell
   # Windows PowerShell
   $env:ANTHROPIC_KEY = "sk-ant-..."     # optional
   .\prompts\e2e-chrome\start-local.ps1
   ```
   Wait for "Ready". The app is at http://localhost:5173 and the API at http://localhost:8307.

2. **Start Claude Code with Chrome** in the repo root:
   ```bash
   claude --chrome
   ```
   (If Claude Code is already running, use `/chrome` to connect it.)

3. **Paste this prompt:**
   ```
   Read prompts/e2e-chrome/flowamaz-e2e-chrome.md and run it end to end in Chrome.
   Follow it exactly, write the results file as you go, and don't change any application code.
   ```

4. Expect the run to take about 60–90 minutes. You can watch it in the Chrome tab it opens. Results
   are saved to `prompts/e2e-chrome/results/e2e-run-<date>.md`, with GIFs of the key journeys.

5. **Stop the app** afterwards: `./prompts/e2e-chrome/start-local.sh stop` (or `… .ps1 stop`).

## Notes
- Each run registers a fresh organisation (the names include a timestamp), so runs don't
  interfere with each other or need a database reset. To start completely clean:
  `docker compose -f infrastructure/docker-compose.dev.yml down -v`.
- The start script uses **development-only** secrets and `EDITION=pro`. Never reuse them anywhere
  else.
- Files:
  - `fixtures/whiteboard-expense-claim.png` — the photo for J12.
  - `fixtures/seed-paid-template.sql` — seeds the paid template for J14 and has cleanup lines at
    the bottom.
- Send the results file back to Claude to triage the failures and fix them.
