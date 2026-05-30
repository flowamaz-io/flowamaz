Read CLAUDE.md.
Update all transactional email templates in EmailService to enterprise quality.
All emails must use HTML with inline CSS (no external stylesheets — email clients strip them).
Use a consistent Flowamaz brand template for all emails.

Base HTML template for all emails:
- Background: #F8F9FA (light gray page background)
- Card: white, max-width 600px, centered, border-radius 8px, subtle shadow
- Header: #0F1117 dark background, Flowamaz logo text in white, tagline below
- Body: Inter font stack, 16px, #374151 text color, 32px padding
- CTA button: #1D9E75 teal background, white text, border-radius 6px, bold
- Footer: #6B7280 gray text, small, links to flowamaz.io, support@flowamaz.io
- Plain text fallback included for every email

--- EMAIL 1: Password Reset ---

Subject: Reset your Flowamaz password

HTML body:
- Header: Flowamaz wordmark on dark background
- H2: "Reset your password"
- Body text: "Hi {firstName}, we received a request to reset the password
  for your Flowamaz account ({email}). Click the button below to choose
  a new password. This link expires in 1 hour."
- CTA button: "Reset Password" → {resetUrl}
- Security note in smaller text: "If you didn't request a password reset,
  you can safely ignore this email. Your password won't change until you
  click the link above and create a new one."
- Footer: "This email was sent to {email}. © 2026 Flowamaz. 
  flowamaz.io · support@flowamaz.io"

Plain text:
"Reset your Flowamaz password

Hi {firstName},

We received a request to reset the password for your account ({email}).

Click the link below to reset your password (expires in 1 hour):
{resetUrl}

If you didn't request this, ignore this email — your password won't change.

© 2026 Flowamaz — flowamaz.io"

--- EMAIL 2: Welcome / Registration ---

Subject: Welcome to Flowamaz — your 14-day trial has started

HTML body:
- Header: Flowamaz wordmark on dark background
- H2: "Welcome to Flowamaz, {firstName}!"
- Body: "Your organisation '{orgName}' is set up and your 14-day free trial
  has started. You have full access to all features during your trial — no
  credit card required."
- 3 feature highlights in a row (inline table layout for email):
  ⚡ Automate in plain English
  🔌 Connect any system  
  ✓ Human approvals built in
- CTA button: "Open Flowamaz" → {appUrl}
- "Need help getting started? Reply to this email or visit our docs at
  docs.flowamaz.io"
- Footer with unsubscribe note: "You're receiving this because you created
  a Flowamaz account. © 2026 Flowamaz."

--- EMAIL 3: Human Gate Approval Request ---

Subject: Action required: {workflowName} needs your approval

HTML body:
- Header: Flowamaz wordmark + amber accent bar below (visual urgency signal)
- H2: "Your approval is needed"
- Details box (light gray background, rounded):
  Workflow: {workflowName}
  Submitted by: {submitterName}
  Submitted: {submittedAt}
  {gateDescription if provided}
- Two CTA buttons side by side (inline table):
  [✓ Approve] teal button → {approveUrl}
  [✗ Reject] light gray button with red text → {rejectUrl}
- Note: "This approval link expires in {expiryHours} hours. You can also
  review this request at {portalUrl}"
- Footer: "Sent by Flowamaz on behalf of {orgName}"

--- EMAIL 4: Gate Decision Confirmation (to submitter) ---

Subject: ✓ Approved — {workflowName} | ✗ Rejected — {workflowName}

HTML body:
- Header: green accent bar (approved) or red accent bar (rejected)
- H2: "Your request was {approved/rejected}"
- Details: workflow name, decided by, decided at, note (if provided)
- CTA: "View in Flowamaz" → instance detail URL
- Footer standard

--- EMAIL 5: Trial Expiry Warning (7 days before) ---

Subject: Your Flowamaz trial ends in 7 days

HTML body:
- Header with amber accent
- H2: "Your trial ends on {expiryDate}"
- Body: "Your 14-day trial for {orgName} ends in 7 days. Upgrade now to
  keep your workflows running without interruption."
- Trial usage summary box:
  Workflows created: {count}
  Runs completed: {count}
  Team members: {count}
- CTA button: "Upgrade Now" → flowamaz.io/pricing
- "Questions? Reply to this email — we're happy to help."

Implementation:
1. Create EmailTemplateService with methods for each template
   that return (HtmlBody: string, TextBody: string, Subject: string)
2. Use string interpolation — no templating library needed for Phase 5
3. Update EmailService.SendAsync to accept pre-built subject/html/text
   OR update all callers to use EmailTemplateService first
4. Update AuthService to use the new password reset template
5. Update OrganisationService to use the new welcome template
6. Update HumanGateNodeWorker to use the new gate approval template

Unit tests:
- EmailTemplateService: password reset template contains resetUrl
- EmailTemplateService: welcome template contains orgName and firstName
- EmailTemplateService: gate approval template has both approve and reject URLs

After all changes:
dotnet build -- 0 errors 0 warnings
dotnet test -- all pass

git add . && git commit -m "feat(email): enterprise HTML email templates for all transactional emails" && git push origin develop