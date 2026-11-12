using Flowamaz.Api.Validators;
using FluentAssertions;

namespace Flowamaz.Tests.Unit.Api;

/// <summary>Billing return URLs: https on the platform's own host and port only (localhost allowed in Development).</summary>
public sealed class RedirectUrlPolicyTests
{
    private const string Base = "https://app.flowamaz.io";

    [Theory]
    [InlineData("https://app.flowamaz.io/settings/billing", true)]
    [InlineData("https://APP.flowamaz.io/x", true)]
    [InlineData("https://app.flowamaz.io:443/x", true)]            // explicit default port = no port
    [InlineData("http://app.flowamaz.io/settings/billing", false)] // downgrade to http
    [InlineData("https://app.flowamaz.io:8443/x", false)]          // same host, another service
    [InlineData("https://evil.example/x", false)]
    [InlineData("https://app.flowamaz.io.evil.example/x", false)]
    [InlineData("https://user@app.flowamaz.io/x", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/settings/billing", false)]
    [InlineData("http://localhost:5173/x", false)]                 // localhost only in Development
    public void Production(string url, bool allowed) =>
        RedirectUrlPolicy.IsAllowed(url, Base, isDevelopment: false).Should().Be(allowed);

    [Theory]
    [InlineData("http://localhost:5173/settings/billing", true)]
    [InlineData("http://127.0.0.1:5173/x", true)]
    [InlineData("http://app.flowamaz.io/x", false)]                // port 80 isn't the https base URL's 443
    [InlineData("https://app.flowamaz.io/x", true)]
    [InlineData("ftp://localhost/x", false)]
    public void Development(string url, bool allowed) =>
        RedirectUrlPolicy.IsAllowed(url, Base, isDevelopment: true).Should().Be(allowed);

    [Fact]
    public void A_base_url_with_an_explicit_port_needs_that_port() =>
        RedirectUrlPolicy.IsAllowed("https://staging.flowamaz.io/x", "https://staging.flowamaz.io:8443", false).Should().BeFalse();
}
