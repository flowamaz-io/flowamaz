using System.Net;

namespace Flowamaz.Application.Email;

/// <summary>
/// Builds enterprise-quality HTML + plain-text bodies for all transactional emails.
/// All templates use inline CSS and table-based layout for broad email-client compatibility.
/// Returns (Subject, HtmlBody, TextBody) — no external templating library.
/// </summary>
public static class EmailTemplateService
{
    private const string AppUrl = "https://app.flowamaz.io";
    private const string DocsUrl = "https://docs.flowamaz.io";
    private const string SupportEmail = "support@flowamaz.io";
    private const string PricingUrl = "https://flowamaz.io/pricing";

    // ── Public template methods ───────────────────────────────────────────────

    public static (string Subject, string HtmlBody, string TextBody) PasswordReset(
        string firstName, string email, string resetUrl)
    {
        const string subject = "Reset your Flowamaz password";

        var html = WrapBody(
            accentBar: null,
            body: $"""
                <h2 style="margin:0 0 16px;font-size:24px;font-weight:700;color:#111827">Reset your password</h2>
                <p style="margin:0 0 16px;font-size:16px;color:#374151;line-height:1.6">
                  Hi {Enc(firstName)}, we received a request to reset the password for your
                  Flowamaz account ({Enc(email)}). Click the button below to choose a new password.
                  This link expires in 1 hour.
                </p>
                <p style="margin:0 0 32px">{TealButton(resetUrl, "Reset Password")}</p>
                <p style="margin:0;font-size:13px;color:#6B7280;line-height:1.5">
                  If you didn't request a password reset, you can safely ignore this email.
                  Your password won't change until you click the link above and create a new one.
                </p>
                """,
            footer: $"This email was sent to {Enc(email)}. &copy; 2026 Flowamaz.<br>" +
                    $"<a href=\"https://flowamaz.io\" style=\"color:#6B7280\">flowamaz.io</a> &middot; " +
                    $"<a href=\"mailto:{SupportEmail}\" style=\"color:#6B7280\">{SupportEmail}</a>");

        var text = $"""
            Reset your Flowamaz password

            Hi {firstName},

            We received a request to reset the password for your account ({email}).

            Click the link below to reset your password (expires in 1 hour):
            {resetUrl}

            If you didn't request this, ignore this email — your password won't change.

            © 2026 Flowamaz — flowamaz.io
            """;

        return (subject, html, text);
    }

    public static (string Subject, string HtmlBody, string TextBody) Welcome(
        string firstName, string orgName)
    {
        const string subject = "Welcome to Flowamaz — your 14-day trial has started";

        var html = WrapBody(
            accentBar: null,
            body: $"""
                <h2 style="margin:0 0 16px;font-size:24px;font-weight:700;color:#111827">Welcome to Flowamaz, {Enc(firstName)}!</h2>
                <p style="margin:0 0 16px;font-size:16px;color:#374151;line-height:1.6">
                  Your organisation '<strong>{Enc(orgName)}</strong>' is set up and your 14-day free trial
                  has started. You have full access to all features during your trial — no credit card required.
                </p>
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="margin:24px 0">
                  <tr>
                    <td align="center" style="padding:16px 8px;background:#F9FAFB;border-radius:8px">
                      <p style="margin:0;font-size:20px">&#x26A1;</p>
                      <p style="margin:4px 0 0;font-size:13px;color:#374151;font-weight:600">Automate in plain English</p>
                    </td>
                    <td width="8"></td>
                    <td align="center" style="padding:16px 8px;background:#F9FAFB;border-radius:8px">
                      <p style="margin:0;font-size:20px">&#x1F50C;</p>
                      <p style="margin:4px 0 0;font-size:13px;color:#374151;font-weight:600">Connect any system</p>
                    </td>
                    <td width="8"></td>
                    <td align="center" style="padding:16px 8px;background:#F9FAFB;border-radius:8px">
                      <p style="margin:0;font-size:20px">&#x2713;</p>
                      <p style="margin:4px 0 0;font-size:13px;color:#374151;font-weight:600">Human approvals built in</p>
                    </td>
                  </tr>
                </table>
                <p style="margin:0 0 32px">{TealButton(AppUrl, "Open Flowamaz")}</p>
                <p style="margin:0;font-size:14px;color:#6B7280">
                  Need help getting started? Reply to this email or visit our docs at
                  <a href="{DocsUrl}" style="color:#1D9E75">{DocsUrl}</a>
                </p>
                """,
            footer: "You're receiving this because you created a Flowamaz account. &copy; 2026 Flowamaz.");

        var text = $"""
            Welcome to Flowamaz — your 14-day trial has started

            Hi {firstName},

            Your organisation '{orgName}' is set up and your 14-day free trial has started.
            You have full access to all features during your trial — no credit card required.

            ⚡ Automate in plain English
            🔌 Connect any system
            ✓ Human approvals built in

            Open Flowamaz: {AppUrl}

            Need help getting started? Reply to this email or visit {DocsUrl}

            © 2026 Flowamaz
            """;

        return (subject, html, text);
    }

    public static (string Subject, string HtmlBody, string TextBody) GateApproval(
        string workflowName, string gateDescription, string approveUrl, string rejectUrl,
        int expiryHours, string portalUrl)
    {
        var subject = $"Action required: {workflowName} needs your approval";

        var descriptionRow = string.IsNullOrWhiteSpace(gateDescription)
            ? string.Empty
            : $"""<tr><td style="padding:4px 0;font-size:14px;color:#374151"><strong>Details:</strong> {Enc(gateDescription)}</td></tr>""";

        var html = WrapBody(
            accentBar: AmberBar(),
            body: $"""
                <h2 style="margin:0 0 16px;font-size:24px;font-weight:700;color:#111827">Your approval is needed</h2>
                <table role="presentation" width="100%" cellspacing="0" cellpadding="12" border="0"
                       style="margin:0 0 24px;background:#F9FAFB;border-radius:8px">
                  <tr><td style="padding:4px 0;font-size:14px;color:#374151"><strong>Workflow:</strong> {Enc(workflowName)}</td></tr>
                  {descriptionRow}
                </table>
                <table role="presentation" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 24px">
                  <tr>
                    <td>{TealButton(approveUrl, "&#x2713; Approve")}</td>
                    <td width="12"></td>
                    <td><a href="{rejectUrl}"
                           style="display:inline-block;background:#F3F4F6;color:#DC2626;padding:12px 28px;border-radius:6px;text-decoration:none;font-weight:700;font-size:15px">&#x2717; Reject</a></td>
                  </tr>
                </table>
                <p style="margin:0;font-size:13px;color:#6B7280;line-height:1.5">
                  This approval link expires in {expiryHours} hour{(expiryHours == 1 ? "" : "s")}.
                  You can also review this request at <a href="{portalUrl}" style="color:#1D9E75">{portalUrl}</a>
                </p>
                """,
            footer: "Sent by Flowamaz");

        var text = $"""
            Action required: {workflowName} needs your approval

            Your approval is needed

            Workflow: {workflowName}
            {(string.IsNullOrWhiteSpace(gateDescription) ? "" : $"Details: {gateDescription}\n")}
            Approve: {approveUrl}
            Reject:  {rejectUrl}

            This approval link expires in {expiryHours} hour{(expiryHours == 1 ? "" : "s")}.
            You can also review this request at {portalUrl}

            Sent by Flowamaz
            """;

        return (subject, html, text);
    }

    public static (string Subject, string HtmlBody, string TextBody) GateDecisionConfirmation(
        string workflowName, bool approved, string decidedBy, DateTime decidedAt,
        string? note, string instanceUrl)
    {
        var verb = approved ? "Approved" : "Rejected";
        var subject = approved
            ? $"✓ Approved — {workflowName}"
            : $"✗ Rejected — {workflowName}";

        var noteRow = string.IsNullOrWhiteSpace(note)
            ? string.Empty
            : $"""<tr><td style="padding:4px 0;font-size:14px;color:#374151"><strong>Note:</strong> {Enc(note)}</td></tr>""";

        var html = WrapBody(
            accentBar: approved ? GreenBar() : RedBar(),
            body: $"""
                <h2 style="margin:0 0 16px;font-size:24px;font-weight:700;color:#111827">Your request was {verb.ToLowerInvariant()}</h2>
                <table role="presentation" width="100%" cellspacing="0" cellpadding="12" border="0"
                       style="margin:0 0 24px;background:#F9FAFB;border-radius:8px">
                  <tr><td style="padding:4px 0;font-size:14px;color:#374151"><strong>Workflow:</strong> {Enc(workflowName)}</td></tr>
                  <tr><td style="padding:4px 0;font-size:14px;color:#374151"><strong>Decided by:</strong> {Enc(decidedBy)}</td></tr>
                  <tr><td style="padding:4px 0;font-size:14px;color:#374151"><strong>Decided at:</strong> {decidedAt:yyyy-MM-dd HH:mm} UTC</td></tr>
                  {noteRow}
                </table>
                <p style="margin:0 0 32px">{TealButton(instanceUrl, "View in Flowamaz")}</p>
                """,
            footer: $"&copy; 2026 Flowamaz. <a href=\"https://flowamaz.io\" style=\"color:#6B7280\">flowamaz.io</a>");

        var text = $"""
            {verb} — {workflowName}

            Your request was {verb.ToLowerInvariant()}.

            Workflow: {workflowName}
            Decided by: {decidedBy}
            Decided at: {decidedAt:yyyy-MM-dd HH:mm} UTC
            {(string.IsNullOrWhiteSpace(note) ? "" : $"Note: {note}\n")}
            View in Flowamaz: {instanceUrl}

            © 2026 Flowamaz — flowamaz.io
            """;

        return (subject, html, text);
    }

    public static (string Subject, string HtmlBody, string TextBody) TrialExpiryWarning(
        string orgName, DateTime expiryDate, int workflowCount, int runCount, int memberCount)
    {
        const string subject = "Your Flowamaz trial ends in 7 days";

        var html = WrapBody(
            accentBar: AmberBar(),
            body: $"""
                <h2 style="margin:0 0 16px;font-size:24px;font-weight:700;color:#111827">Your trial ends on {expiryDate:MMMM d, yyyy}</h2>
                <p style="margin:0 0 24px;font-size:16px;color:#374151;line-height:1.6">
                  Your 14-day trial for <strong>{Enc(orgName)}</strong> ends in 7 days. Upgrade now to
                  keep your workflows running without interruption.
                </p>
                <table role="presentation" width="100%" cellspacing="0" cellpadding="12" border="0"
                       style="margin:0 0 24px;background:#F9FAFB;border-radius:8px">
                  <tr><td style="padding:4px 0;font-size:14px;color:#374151"><strong>Workflows created:</strong> {workflowCount}</td></tr>
                  <tr><td style="padding:4px 0;font-size:14px;color:#374151"><strong>Runs completed:</strong> {runCount}</td></tr>
                  <tr><td style="padding:4px 0;font-size:14px;color:#374151"><strong>Team members:</strong> {memberCount}</td></tr>
                </table>
                <p style="margin:0 0 32px">{TealButton(PricingUrl, "Upgrade Now")}</p>
                <p style="margin:0;font-size:14px;color:#6B7280">
                  Questions? Reply to this email — we're happy to help.
                </p>
                """,
            footer: $"&copy; 2026 Flowamaz. <a href=\"https://flowamaz.io\" style=\"color:#6B7280\">flowamaz.io</a>");

        var text = $"""
            Your Flowamaz trial ends in 7 days

            Your trial ends on {expiryDate:MMMM d, yyyy}

            Your 14-day trial for '{orgName}' ends in 7 days. Upgrade now to keep your workflows
            running without interruption.

            Workflows created: {workflowCount}
            Runs completed:    {runCount}
            Team members:      {memberCount}

            Upgrade now: {PricingUrl}

            Questions? Reply to this email — we're happy to help.

            © 2026 Flowamaz — flowamaz.io
            """;

        return (subject, html, text);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string WrapBody(string? accentBar, string body, string footer) =>
        $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1">
        </head>
        <body style="margin:0;padding:0;background:#F8F9FA;font-family:Inter,-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif">
          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0">
            <tr><td align="center" style="padding:32px 16px">
              <table role="presentation" width="600" cellspacing="0" cellpadding="0" border="0"
                     style="max-width:600px;width:100%;background:#ffffff;border-radius:8px;box-shadow:0 1px 3px rgba(0,0,0,0.1)">
                <tr>
                  <td style="background:#0F1117;padding:28px 32px;border-radius:8px 8px 0 0">
                    <p style="margin:0;font-size:22px;font-weight:700;color:#ffffff;letter-spacing:-0.5px">Flowamaz</p>
                    <p style="margin:4px 0 0;font-size:12px;color:#9CA3AF">AI-native workflow automation</p>
                  </td>
                </tr>
                {(accentBar ?? string.Empty)}
                <tr><td style="padding:32px">{body}</td></tr>
                <tr>
                  <td style="padding:24px 32px;border-top:1px solid #F3F4F6">
                    <p style="margin:0;font-size:12px;color:#6B7280;line-height:1.5">{footer}</p>
                  </td>
                </tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;

    private static string TealButton(string url, string label) =>
        $"""<a href="{url}" style="display:inline-block;background:#1D9E75;color:#ffffff;padding:12px 28px;border-radius:6px;text-decoration:none;font-weight:700;font-size:15px">{label}</a>""";

    private static string AmberBar() =>
        """<tr><td style="background:#F59E0B;height:4px;padding:0;font-size:0;line-height:0">&nbsp;</td></tr>""";

    private static string GreenBar() =>
        """<tr><td style="background:#10B981;height:4px;padding:0;font-size:0;line-height:0">&nbsp;</td></tr>""";

    private static string RedBar() =>
        """<tr><td style="background:#EF4444;height:4px;padding:0;font-size:0;line-height:0">&nbsp;</td></tr>""";

    private static string Enc(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);
}
