using System.Net;

namespace Flowamaz.Application.Email;

/// <summary>The human-gate approval email (used by HumanGateNodeWorker). The product's EmailTemplateService delegates to it.</summary>
public static class GateEmailTemplates
{
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
