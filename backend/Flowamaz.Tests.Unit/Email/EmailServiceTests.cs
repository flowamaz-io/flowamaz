using FluentAssertions;
using Flowamaz.Core.Configuration;
using Flowamaz.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Flowamaz.Tests.Unit.Email;

public class EmailServiceTests
{
    [Fact]
    public async Task SendAsync_with_no_api_key_logs_and_returns_false_does_not_throw()
    {
        var httpFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        // Strict mock — if ResendApiKey is empty the service must never reach the HTTP factory.
        var options = Options.Create(new EmailOptions { ResendApiKey = "" });
        var config = new ConfigurationBuilder().Build();
        var service = new EmailService(httpFactory.Object, options, config, NullLogger<EmailService>.Instance);

        var result = await service.SendAsync(
            to: "user@example.com",
            subject: "test",
            htmlBody: "<p>hi</p>");

        result.Should().BeFalse();
        httpFactory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }
}
