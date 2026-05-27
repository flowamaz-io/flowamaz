using Flowamaz.Application.Email;
using FluentAssertions;

namespace Flowamaz.Tests.Unit.Email;

public class EmailTemplateServiceTests
{
    [Fact]
    public void PasswordReset_template_contains_resetUrl()
    {
        var resetUrl = "https://app.flowamaz.io/reset-password?token=abc123&org=acme";

        var (subject, html, text) = EmailTemplateService.PasswordReset("Alice", "alice@acme.com", resetUrl);

        subject.Should().Be("Reset your Flowamaz password");
        html.Should().Contain(resetUrl);
        text.Should().Contain(resetUrl);
    }

    [Fact]
    public void PasswordReset_template_contains_firstName_and_email()
    {
        var (_, html, text) = EmailTemplateService.PasswordReset("Bob", "bob@acme.com", "https://example.com");

        html.Should().Contain("Bob");
        html.Should().Contain("bob@acme.com");
        text.Should().Contain("Bob");
        text.Should().Contain("bob@acme.com");
    }

    [Fact]
    public void Welcome_template_contains_orgName_and_firstName()
    {
        var (subject, html, text) = EmailTemplateService.Welcome("Carol", "Acme Corp");

        subject.Should().Be("Welcome to Flowamaz — your 14-day trial has started");
        html.Should().Contain("Carol");
        html.Should().Contain("Acme Corp");
        text.Should().Contain("Carol");
        text.Should().Contain("Acme Corp");
    }

    [Fact]
    public void GateApproval_template_contains_both_approve_and_reject_urls()
    {
        var approveUrl = "https://app.flowamaz.io/api/v1/gates/abc/approve?sig=xyz";
        var rejectUrl  = "https://app.flowamaz.io/api/v1/gates/abc/reject?sig=xyz";

        var (subject, html, text) = EmailTemplateService.GateApproval(
            "Invoice Approval", "Review the Q2 invoice", approveUrl, rejectUrl, 48, "https://app.flowamaz.io");

        subject.Should().Contain("Invoice Approval");
        html.Should().Contain(approveUrl);
        html.Should().Contain(rejectUrl);
        text.Should().Contain(approveUrl);
        text.Should().Contain(rejectUrl);
    }
}
