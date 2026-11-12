using Flowamaz.Application.Platform;
using Flowamaz.Core.Configuration;
using Flowamaz.Infrastructure.Jobs;
using Flowamaz.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Flowamaz.Tests.Unit.Platform;

/// <summary>Staff organisations from configuration, audit retention settings and the SMTP email path.</summary>
public sealed class ClosedGapsUnitTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    public void Staff_organisations_come_from_a_comma_separated_value_or_a_list()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        PlatformStaff.IsStaffOrg(Config(("Platform:StaffOrgIds", $"{a}, {b}")), b).Should().BeTrue();
        PlatformStaff.IsStaffOrg(Config(("Platform:StaffOrgIds:0", a.ToString())), a).Should().BeTrue();
        PlatformStaff.IsStaffOrg(Config(("Platform:StaffOrgIds", "not-a-guid")), a).Should().BeFalse();
        PlatformStaff.IsStaffOrg(Config(), a).Should().BeFalse("with nothing configured there are no staff");
        PlatformStaff.IsStaffOrg(Config(("Platform:StaffOrgIds", Guid.Empty.ToString())), Guid.Empty).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, AuditRetentionJob.DefaultRetentionDays)]
    [InlineData("-5", AuditRetentionJob.DefaultRetentionDays)]
    [InlineData("0", 0)]
    [InlineData("30", 365)]
    [InlineData("1000", 1000)]
    public void Audit_retention_is_a_year_or_more_unless_unlimited(string? configured, int expected) =>
        AuditRetentionJob.RetentionDays(Config(("Audit:RetentionDays", configured))).Should().Be(expected);

    [Fact]
    public async Task With_an_smtp_host_email_goes_over_smtp_and_a_failure_returns_false()
    {
        var service = new EmailService(
            Mock.Of<IHttpClientFactory>(),
            Options.Create(new EmailOptions { SmtpHost = "127.0.0.1", SmtpPort = 1, FromAddress = "noreply@flowamaz.local" }),
            Config(),
            NullLogger<EmailService>.Instance);

        (await service.SendAsync("ana@acme.test", "Reset your password", "<p>Hi</p>", "Hi")).Should().BeFalse(
            "nothing listens on port 1; the failure is logged, not thrown, and Resend isn't tried");
    }

    [Fact]
    public async Task Smtp_email_is_multipart_with_the_html_part_labelled_html()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var received = Task.Run(async () =>
        {
            // A minimal SMTP server: accept every command, keep the DATA.
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream);
            using var writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\r\n" };
            await writer.WriteLineAsync("220 test");
            var data = new System.Text.StringBuilder();
            var inData = false;
            while (await reader.ReadLineAsync() is { } line)
            {
                if (inData)
                {
                    if (line == ".") { inData = false; await writer.WriteLineAsync("250 ok"); continue; }
                    data.AppendLine(line);
                    continue;
                }
                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)) await writer.WriteLineAsync("250 test");
                else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase)) { inData = true; await writer.WriteLineAsync("354 go"); }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase)) { await writer.WriteLineAsync("221 bye"); break; }
                else await writer.WriteLineAsync("250 ok");
            }
            return data.ToString();
        });

        var service = new EmailService(
            Mock.Of<IHttpClientFactory>(),
            Options.Create(new EmailOptions { SmtpHost = "127.0.0.1", SmtpPort = port, FromAddress = "noreply@flowamaz.local" }),
            Config(),
            NullLogger<EmailService>.Instance);

        (await service.SendAsync("ana@acme.test", "Reset your password", "<p>Hi</p>", "Hi")).Should().BeTrue();
        var mime = await received.WaitAsync(TimeSpan.FromSeconds(10));
        listener.Stop();

        mime.Should().Contain("multipart/alternative");
        mime.IndexOf("text/plain", StringComparison.Ordinal).Should().BeLessThan(mime.IndexOf("text/html", StringComparison.Ordinal),
            "plain text first, HTML last, so clients render the HTML");
    }
}
