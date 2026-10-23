# Flowamaz — End-to-end test in Chrome (Claude in Chrome)

You are a QA engineer running a manual end-to-end test of Flowamaz in the user's Chrome, using the
Claude in Chrome tools. Follow the journeys below **in order**, check every **Expect** line, and
record what actually happened. You test; you do not fix code. Stripe payment is **out of scope**:
never enter card details and never complete a Stripe page.

---

## 0. Ground rules

**Browser**
- Start with `tabs_context_mcp`. Open **one new tab** for the whole run with `tabs_create_mcp`.
  Never touch the user's other tabs.
- Load the browser tools you need in one ToolSearch call: `tabs_context_mcp`, `tabs_create_mcp`,
  `navigate`, `computer`, `read_page`, `find`, `form_input`, `get_page_text`, `file_upload`,
  `gif_creator`, `read_console_messages`, `resize_window`.
- Prefer `find` / `read_page` to locate elements by their visible text, then click. Many icon-only
  buttons are identified by their `title` or `aria-label`; this script gives those in quotes.
- Never trigger browser `alert`/`confirm` dialogs. The app uses in-page modals, so this shouldn't
  happen. If a native dialog appears, stop and tell the user.
- If the same action fails 3 times, mark the step **FAIL** with a screenshot and move on. Don't loop.
- Toasts disappear after about 5 seconds. Read them right after the action.

**Recording**
- Record a GIF for journeys J4–J7 and J10 with `gif_creator`, named `J<n>-<short-name>.gif`. Capture
  a few extra frames before and after each action.
- After each journey, run `read_console_messages` with pattern `error|Error|failed`. Record any
  errors other than the one expected 401 on the register/login page (that's the session check).

**Results file** (create it before J0; update it after each journey, not only at the end)
- Path: `prompts/e2e-chrome/results/e2e-run-<YYYY-MM-DD-HHmm>.md`
- Start it with: date and time, git branch and commit (`git rev-parse --short HEAD`), and the
  **AI mode** (see J0).
- One table row per check:

  | ID | Check | Result | Notes / evidence |
  |----|-------|--------|------------------|
  | J1.3 | Register lands on onboarding | PASS | |

  Result is one of: PASS, FAIL, BLOCKED (couldn't run, say why), SKIP (out of scope).
- For each FAIL, add a bug entry at the end:
  - **Title:** what is wrong, in one line
  - **Severity:** Critical (data loss, security, journey impossible), High (feature broken, no
    workaround), Medium (broken with workaround / wrong message), Low (cosmetic)
  - **Steps:** exact steps to reproduce
  - **Expected / Actual:** the Expect line versus what you saw (quote on-screen text exactly)
  - **Evidence:** screenshot or GIF name, console errors, the correlationId if the error showed one
  - **API log:** the matching lines from `prompts/e2e-chrome/results/logs/api.log` (search for the
    correlationId or the time)

**Test data** (`<RUN>` = the current time as `MMddHHmm`, e.g. 10231945)

| Item | Value |
|------|-------|
| Organisation name | `Acme E2E <RUN>` (its URL slug is generated, e.g. `acme-e2e-10231945`; record it) |
| Owner | `Olivia Owner`, `olivia+<RUN>@acme.test` |
| Password | `E2e!Passw0rd-2026` |
| Plan | **Pro** |
| Workspaces | `Finance Ops` (onboarding), `HR Ops` (J3) |
| Trigger payload | `{ "amount": 47500, "requester": "Anita Kumar" }` |
| Test HTTP endpoint for action steps | `http://localhost:8307/health` (local API, always up) |

---

## J0 — Pre-flight

1. In a shell, run `curl -s http://localhost:8307/health` and `curl -s -o /dev/null -w "%{http_code}" http://localhost:5173/api/v1/templates`.
   **Expect:** health returns a healthy status and the proxy returns `200`. If not, stop and ask
   the user to run `prompts/e2e-chrome/start-local.sh` (or `.ps1` on Windows).
2. Decide the AI mode: search `prompts/e2e-chrome/results/logs/api.log` for `AI STUB MODE`.
   - **Found:** AI mode = **stub (no key)**. In J11–J13, steps marked *[key]* are BLOCKED; record
     them as `BLOCKED — no Anthropic key`, not FAIL.
   - **Not found:** AI mode = **live**. Run every step.
3. Open `http://localhost:8307/scalar`. **Expect:** the API reference page loads.

## J1 — Register an organisation and onboard

1. Navigate to `http://localhost:5173/register`. **Expect:** heading "Start your 14-day free trial".
2. Negative check: fill every field, but put a different value in "Confirm password".
   **Expect:** inline text "Passwords do not match." and you stay on the page.
3. Fill the form:
   - Plan: click the **Pro** card.
   - "Organisation name": `Acme E2E <RUN>`. **Expect:** the "Organisation URL" fills itself in.
   - "Full name": `Olivia Owner`; "Email": the owner email; "Password" and "Confirm password":
     the password.
   - Click **"Create organisation"**.
   **Expect:** you land on `/onboarding`. Record the organisation URL slug for later logins.
4. Onboarding step 1: "Workspace name" `Finance Ops`, then **"Continue"**.
   **Expect:** step 2 "Your workflows will build themselves", listing six creation methods.
5. Click **"Continue"**, then **"Skip for now"** on "Invite your first team member".
   **Expect:** the Dashboard, showing "Welcome back, Olivia".
6. If a product tour card appears, close it with its X ("Skip tour"). If that doesn't respond,
   press Escape, then click "Next" until the last step's "Explore on my own".
   **Expect:** the tour is gone.
7. **Expect:** the "Get started with Flowamaz" checklist shows "0 of 4 complete" or similar.
   Record the plan label under the user's name at the bottom of the sidebar (expected "Pro Plan").
   If it says "Community", record that as a FAIL (wrong plan shown).

## J2 — Sign out, sign in, protected routes

1. Open the user menu (button "Account: Olivia Owner") and click **"Logout"**.
   **Expect:** you are on `/login`.
2. Navigate to `http://localhost:5173/workflows`.
   **Expect:** you're sent to `/login?redirect=/workflows`.
3. Try a wrong password with the right email and organisation URL.
   **Expect:** an error message that says what to do, not "Something went wrong". Quote it.
4. Sign in correctly. **Expect:** you land on `/workflows` (the redirect target), not onboarding.
5. Open `/forgot-password`, enter the email and organisation URL, and click **"Send reset link"**.
   **Expect:** "If that email exists, a reset link has been sent. Check your inbox." No email is
   sent locally. In the API log, confirm a line containing `NOT sent` for the reset email.

## J3 — Workspaces

1. Click the workspace switcher at the top of the sidebar ("Workspace: Finance Ops").
   **Expect:** a dropdown showing Finance Ops with an **Admin** role badge.
2. Click **"Create workspace"**. Enter "Workspace name" `HR Ops` and check the URL preview shows
   `flowamaz.io/hr-ops`. Click **"Create workspace"**.
   **Expect:** the modal closes (no toast is expected).
3. Open `/workspaces`. **Expect:** two cards, Finance Ops and HR Ops, each with a role badge, a
   workflow count and an **"Open"** button.
4. Switch back to **Finance Ops** with the switcher. Do all the following journeys in Finance Ops.

## J4 — Install a template and explore the canvas  *(GIF)*

1. Open `/library`, then the **"Templates"** tab. **Expect:** 10 official templates, each card
   with a price badge reading **"Free"**.
2. Click **"Preview"** on **Purchase Approval**. **Expect:** the template detail page shows "This
   template uses" (a non-empty list of node types) and the YAML under "Workflow definition".
3. Click **"Install template"**. Name it `Q4 Purchase Approval` and click **"Install"**.
   **Expect:** the editor opens (`/workflows/<id>/edit`) with 7 nodes: Purchase requested,
   Manager approval, Approved?, Finance approval, Create purchase order, Notify requester of
   rejection, Complete. The YAML is on the right and the minimap is at the bottom-right.
4. **Minimap:** click the top-left corner of the minimap, then its centre.
   **Expect:** the main canvas view moves each time.
5. **Grid snap:** click the toolbar button titled "Snap to grid: off".
   **Expect:** its title changes to "Snap to grid: on (20px)". Drag any node a little and release.
   **Expect:** it settles onto the dot grid.
6. **Box select:** hold Shift and drag a rectangle around "Create purchase order" and "Notify
   requester of rejection". **Expect:** the overlay shows "2 nodes selected — Delete removes,
   Ctrl+D duplicates".
7. Press Ctrl+D (Cmd+D on Mac). **Expect:** two copies appear and the save state shows "Unsaved
   changes". Press Ctrl+Z. **Expect:** the copies are removed.
8. **Show in YAML:** right-click "Finance approval" and choose **"Show in YAML"**.
   **Expect:** the YAML editor scrolls to and highlights the `id: finance-approval` line.
9. Click the validation badge in the top bar ("… warning(s)" or "Valid").
   **Expect:** either a warnings panel or the toast "Workflow is valid — ready to publish". Record
   which, and quote any warnings.

## J5 — Configure the steps  *(GIF)*

1. Double-click **"Create purchase order"**. **Expect:** the node configuration panel opens.
2. Set "Connector" to **HTTP/REST** and "Action" to **HTTP GET**. **Expect:** input fields appear,
   with `url` marked as required.
3. In `url` enter `http://localhost:8307/health`, then click **"Save node"**.
4. Repeat steps 1–3 for **"Notify requester of rejection"**.
5. Double-click **"Manager approval"**. In the gate settings check that:
   - "On timeout" offers **Fail the run / Escalate to someone else / Auto-approve / Auto-reject**,
     with **Fail the run** selected.
   - Choosing **"Escalate to someone else"** shows "Escalate to (email)". Entering `not-an-email`
     shows a guidance message.
   - Ticking **Slack** under "Notification channels" shows the "Slack channel" field (placeholder
     "#approvals"), a "Slack account" picker, and the hint "Slack isn't connected — connect it in
     Settings → Credentials…".
   - Ticking **Teams** shows "Teams incoming webhook URL".
   Then put it back: "On timeout" = **Fail the run**, untick Slack and Teams, and click **"Cancel"**
   so nothing is changed.
6. Press Ctrl+S, or click the toolbar save button titled "Save Ctrl+S".
   **Expect:** the save state reads "Saved". There is no toast for this.
7. Reload the page. **Expect:** the two action nodes still have their HTTP/REST settings. Check
   that the YAML shows `connector_id: http-rest` under both.

## J6 — Publish, run, approve, complete  *(GIF)*

1. Open `/workflows`, click **Q4 Purchase Approval**, and click **"Publish"**.
   **Expect:** toast "Workflow published." and the status badge shows Published.
2. Click **"Trigger run"**. In "Payload (JSON, optional)" paste the trigger payload from the test
   data table, then click **"Trigger run"**. **Expect:** toast "Run xxxxxxxx started". Record the
   8-character id.
3. Open `/instances` and click that run. **Expect:** within about 10 seconds the status is
   **Waiting**, and the timeline shows "Purchase requested" then "Manager approval".
4. Check the **variables** tab. **Expect:** `amount` = 47500 and `requester` = Anita Kumar.
5. Open `/gates`. **Expect:** a pending row for "Manager approval". Type `Within budget` into
   "Optional note…" and click **"Approve"**. **Expect:** the row shows Approved (there is no toast).
6. Refresh `/gates`. **Expect:** a new pending row, "Finance approval". Approve it.
7. Go back to the run. **Expect:** within about 15 seconds the status is **Completed**. The
   timeline includes Finance approval, Create purchase order and Complete. It does **not** include
   "Notify requester of rejection" — if it does, that's a Critical FAIL.
8. Open the **narrative** tab and click **"Developer"**. **Expect:** a narrative appears (in stub
   mode it starts with "Status summary").

## J7 — Rejection path and run controls  *(GIF)*

1. Trigger another run of Q4 Purchase Approval with the same payload.
2. In `/gates`, **Reject** "Manager approval" with the note `Over budget`.
   **Expect:** the run reaches **Completed** through "Notify requester of rejection". Finance
   approval is never created. If the run shows **Failed** instead, that's a High FAIL.
3. Trigger a third run. When it reaches **Waiting**, open it and click **"Cancel"**.
   **Expect:** toast "Run cancelled." and status Cancelled. Its approval disappears from the
   **Pending** tab of `/gates`. If it still shows, try **"Approve"** on it. **Expect:** a message
   that the run was cancelled or finished and the approval no longer applies — never a silent
   "Approved".
4. **Test run:** on `/workflows` click the icon titled "Test run" on any Draft workflow (install
   **Leave Request** from Templates first if you have no draft). **Expect:** a "Start a test run"
   modal. Run it. **Expect:** toast "Test run xxxxxxxx started", and the run appears under the
   **Test** tab of `/instances` with a TEST tag.

## J8 — Credentials (secrets are never shown)

1. Open `/settings/credentials` and click **"Add credential"**.
2. Set "Connector" to **HTTP/REST**, "Type" to **Bearer token**, "Name" to `E2E API token`, and
   paste the secret `e2e-secret-should-never-be-visible`. Click **"Save credential"**.
   **Expect:** toast "Credential saved. Its value is encrypted and can never be viewed again."
3. Check that the secret appears nowhere: run `get_page_text` and search for
   `e2e-secret-should-never-be-visible`. **Expect:** not found. Also search `api.log` for it.
   **Expect:** not found. Any match is a **Critical** FAIL.
4. Revoke the credential. **Expect:** the "Revoke credential?" modal, then the row shows revoked
   or disappears.

## J9 — Publish as template (free and paid, without Stripe)

1. On the Q4 Purchase Approval detail page, click **"Publish as template"**.
2. Choose **"Monthly"** pricing and enter `19`. **Expect:** the line "You receive $15.20/mo after
   the 20% Flowamaz fee ($3.80)" (or equivalent) and a "Set up payouts before selling" notice
   linking to Publisher settings, because Stripe isn't connected.
3. Switch to **"Free"**, set "Category" to Finance and add a description, then click
   **"Publish template"**. **Expect:** toast "Template submitted for review."
4. Open `/settings/publisher`. **Expect:** badge "Not connected", the Stripe test-mode hint, and a
   **"Connect with Stripe"** button.
5. Click **"Connect with Stripe"**. **Expect:** because no Stripe key is set locally, an error
   "Couldn't open Stripe onboarding…" that says Stripe isn't configured and names
   `STRIPE_SECRET_KEY`. A blank page or "Something went wrong" is a FAIL. Don't configure Stripe.
6. Open `/settings/purchases`. **Expect:** the empty state "This workspace hasn't bought any
   templates yet" with a "Browse templates" button.

## J10 — Webhook trigger  *(GIF)*

1. Open `/settings/webhooks` and click **"Create webhook"**. Choose **Q4 Purchase Approval** and
   set the description to `E2E webhook`. Click **"Create webhook"**.
   **Expect:** the signing secret is shown once ("Copy your signing secret now — it won't be shown
   again."). Copy the secret and the endpoint id or URL into the results file **as a redacted
   placeholder only**, keeping the real values in your working memory for the next step.
2. In a shell, send a signed request (replace `<ID>` and `<SECRET>`):
   ```bash
   BODY='{"amount":2500,"requester":"Webhook Tester"}'
   SIG=$(node -e "console.log(require('crypto').createHmac('sha256',process.argv[1]).update(process.argv[2]).digest('hex'))" "<SECRET>" "$BODY")
   curl -s -w " [%{http_code}]\n" -X POST http://localhost:5173/webhooks/<ID> -H "Content-Type: application/json" -H "X-Flowamaz-Signature: sha256=$SIG" -d "$BODY"
   ```
   **Expect:** `[202]` with an `instanceId`.
3. Send the same request with `X-Flowamaz-Signature: sha256=deadbeef`.
   **Expect:** `[401]` with an `invalid_signature` message that explains how to sign.
4. In `/instances`, **Expect:** the new run, with trigger shown as Webhook, waiting at Manager
   approval. Cancel it to keep things tidy.

## J11 — Create from plain English

1. Open `/workflows/new` and choose **"Write a description"**.
2. Fill in:
   - "Workflow Name": `Expense Claim Approval`
   - "Purpose": `Employees claim expenses; managers approve.`
   - "Steps": `Employee submits a claim. Manager reviews it. Pay the employee.`
   Leave "Trigger", "Rules & Constraints" and "Systems & AI" empty.
3. Click **"Generate Workflow"**. **Expect:** "A few quick questions before we generate" with 1–3
   questions (for example, what happens on rejection, or what starts the workflow). This works in
   both AI modes. Record the questions.
4. Answer one question, leave the rest blank, and click **"Generate with answers"**.
   - *[key]* **Expect:** within about 20 seconds the editor opens with a workflow that reflects
     the steps you typed.
   - *Stub mode:* **Expect:** an error saying the workflow couldn't be built, with advice on what
     to add. Record it as BLOCKED — no Anthropic key, and quote the message.
5. Editing any field after the questions appear should clear them. Check this once.

## J12 — Create from a whiteboard photo  *[key]*

1. On `/workflows/new` choose **"Upload a photo"** and upload
   `prompts/e2e-chrome/fixtures/whiteboard-expense-claim.png` with `file_upload`.
2. **Expect:** "Analysing your diagram…", then a review list beginning "We found N step(s)…", with
   a confidence percentage and a "Step name" field for each step.
3. Rename one step, click **"Remove"** on another (**Expect:** "Will be removed — connected steps
   are joined up."), then click **"Create workflow (N steps)"**.
   **Expect:** the editor opens and the removed step is not on the canvas.
4. In stub mode, record BLOCKED, along with what the page showed after upload.

## J13 — Other creation methods

1. **"Draw on canvas"** (no AI): set the top "Workflow name" to `Blank Canvas E2E` and choose
   **"Draw on canvas"**. **Expect:** the editor opens straight away (empty or with a starter
   Trigger/End). Drag an **"Act"** tile from the palette onto the canvas. **Expect:** a new Action
   node appears where you dropped it. If drag-and-drop can't be driven from the extension, record
   BLOCKED (tooling), not FAIL.
2. *[key]* **"Paste a conversation"**: choose **Slack** and paste:
   `Raj: new laptop requests go to me first. Mei: if over 3000 finance must sign off. Raj: then IT orders it and tells the employee.`
   Click **"Extract Workflow"**. **Expect:** the editor opens with approval and ordering steps.
   In stub mode, record BLOCKED.
3. Open the Co-pilot (button "✦ Co-pilot ⌘K") in any editor and type
   `Add a 24h timeout to manager-approval` (in Q4 Purchase Approval). Click **"Run"**.
   **Expect:** "Applied pattern…" and a "What changed" preview, or an error that says what to do.
   Record which. Don't apply the change.

## J14 — Paid template is locked until bought (no payment)

This needs a paid template, which only exists once Stripe works. Seed one directly:
```bash
docker exec -i flowamaz-dev-db psql -U flowamaz -d flowamaz < prompts/e2e-chrome/fixtures/seed-paid-template.sql
```
1. Open `/library`, then the **"Templates"** tab. **Expect:** **"E2E Paid Expense Flow"** with a
   price badge of **$49.00**.
2. Click **"Install"** on that card. **Expect:** you're taken to its detail page (not the install
   modal) and see "This is a paid template — buy it for this workspace to unlock install."
3. On the detail page, **Expect:**
   - A **"Buy for $49.00"** button.
   - A disabled "Install unlocks after purchase".
   - The note "One-time payment…".
   - **No YAML shown**: the paid workflow definition must stay hidden. If YAML is visible, that's a
     **High** FAIL.
   - The "This template uses" list is still visible.
4. Click **"Buy for $49.00"**. **Expect:** an error saying paid workflows aren't available because
   Stripe isn't configured. You must **not** be redirected to Stripe. Payment itself is SKIP.
5. Clean up: run the two `DELETE` lines at the bottom of the seed file through the same
   `docker exec … psql` command.

## J15 — Settings, audit, insights

1. **Members** (`/settings/members`): **Expect:** Olivia is listed as Admin with "You". Click
   **"Invite member"**, enter `nobody+<RUN>@acme.test` with role Viewer, and click
   **"Send invite"**. **Expect:** a clear message that the person must already be an organisation
   member. Record the exact text, and note in the results that new people can't yet be invited from
   here (observation, not FAIL).
2. **API keys** (`/settings/api-keys`): click **"Create key"**, name it `E2E key`, tick "Read
   workflows" and create it. **Expect:** "Copy your new API key now — it won't be shown again."
   Close the dialog, reload, and check the key value isn't visible. Then **"Revoke"** it.
   **Expect:** toast "API key revoked."
3. **Workspace settings** (`/settings`): change "Run retention" and click **"Save settings"**.
   **Expect:** toast "Workspace settings saved." Then reload and check the value persisted.
4. **Audit Log** (`/settings/audit`): **Expect:** entries from this run, including the template
   install, gate approved/rejected, credential created/revoked and webhook created. Click
   **"Export CSV"**. **Expect:** toast "Audit log exported."
5. **Dashboard** (`/`): **Expect:** metrics show at least 1 workflow and some runs, and the
   checklist shows "Create your first workflow" and "Trigger your first run" as done.
6. **Weather** (`/weather`) and **Analytics** (`/analytics`): **Expect:** both load with data or
   with a teaching empty state, not a blank page or raw error.
7. **Help:** click "Help" in the sidebar and search `gate`. **Expect:** matching articles. Open one,
   then click "Was this helpful?" thumbs-up. **Expect:** "Thanks for your feedback!"

## J16 — Small screen

1. Resize the window to 390×844 with `resize_window`.
2. **Expect:** the sidebar collapses behind a menu button ("Open menu"). Open it and navigate to
   Instances. **Expect:** the page fits without horizontal scrolling. Take a screenshot.
3. Restore the window to 1440×900.

## J17 — Wrap-up

1. Log out. **Expect:** `/login`.
2. Finish the results file:
   - A summary line: total checks, PASS, FAIL, BLOCKED, SKIP.
   - The bug list, sorted by severity.
   - A short **"Observations"** list for anything that worked but felt wrong (confusing text,
     slow steps over 5 seconds, layout problems). Keep these separate from failures.
3. Tell the user, in a few lines:
   - Where the results file is.
   - The counts.
   - The top three issues.
   - Which journeys were BLOCKED, and why.
   Don't paste the whole file.

---

### Known, expected behaviour (don't log these as bugs)
- No toast after saving in the editor or approving/rejecting a gate. The status text or row
  updates instead.
- Emails (password reset, gate notifications, invites) are not sent locally. The API log shows
  `NOT sent`.
- A 401 in the console on `/register` or `/login` is the background session check.
- In stub AI mode, plain English, photo and conversation generation can't produce a workflow.
  Clarifying questions still work.
- Stripe Checkout and Connect onboarding are out of scope: SKIP them.
