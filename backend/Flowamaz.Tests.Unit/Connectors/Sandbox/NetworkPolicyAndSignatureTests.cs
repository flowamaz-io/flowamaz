using System.Net;
using FluentAssertions;
using Flowamaz.Core.Models;
using Flowamaz.Core.Security;

namespace Flowamaz.Tests.Unit.Connectors.Sandbox;

[Trait("Category", "Sandbox")]
public class NetworkPolicyAndSignatureTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.20.0.5")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:10.0.0.1")]
    public void IsBlocked_InternalAddresses(string ip) =>
        NetworkPolicy.IsBlocked(IPAddress.Parse(ip)).Should().BeTrue();

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("140.82.112.3")]
    [InlineData("2606:4700:4700::1111")]
    public void IsBlocked_PublicAddresses_Allowed(string ip) =>
        NetworkPolicy.IsBlocked(IPAddress.Parse(ip)).Should().BeFalse();

    [Theory]
    [InlineData("slack.com", true)]
    [InlineData("hooks.slack.com", true)]
    [InlineData("SLACK.COM.", true)]
    [InlineData("evilslack.com", false)]
    [InlineData("slack.com.evil.io", false)]
    public void HostAllowed_MatchesDomainAndSubdomains(string host, bool expected) =>
        NetworkPolicy.HostAllowed(host, ["slack.com"]).Should().Be(expected);

    [Fact]
    public void HostAllowed_NoAllowlist_AllowsAnyHost() =>
        NetworkPolicy.HostAllowed("example.org", null).Should().BeTrue();

    [Fact]
    public void Signature_RoundTrips_AndRejectsTamperingSkewAndWrongSecret()
    {
        const string secret = "0123456789abcdef0123456789abcdef";
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        var sig = RunnerSignature.Compute(secret, ts, "{\"a\":1}");
        var tsText = ts.ToString(System.Globalization.CultureInfo.InvariantCulture);

        RunnerSignature.Verify(secret, tsText, sig, "{\"a\":1}", now).Should().BeTrue();
        RunnerSignature.Verify(secret, tsText, sig, "{\"a\":2}", now).Should().BeFalse("body was altered");
        RunnerSignature.Verify(secret + "x", tsText, sig, "{\"a\":1}", now).Should().BeFalse("secret differs");
        RunnerSignature.Verify(secret, tsText, sig, "{\"a\":1}", now.AddMinutes(5)).Should().BeFalse("replayed outside the window");
        RunnerSignature.Verify(secret, null, null, "{\"a\":1}", now).Should().BeFalse("unsigned");
    }
}
