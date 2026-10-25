using System.IO.Compression;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Web;
using System.Xml;
using FluentAssertions;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Services.Sso;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flowamaz.Tests.Unit.Auth.Sso;

/// <summary>
/// The SAML service provider against real signed responses — and the classic attacks: tampering, signature wrapping,
/// a different key, unsigned, weak algorithms, wrong audience/issuer/recipient, expired, failed status, DTDs.
/// </summary>
public sealed class SamlProcessorTests : IDisposable
{
    private readonly SamlTestIdp _idp = new();
    private readonly SamlProcessor _sp = new(NullLogger<SamlProcessor>.Instance);

    private SamlExpectations Expected(string? cert = null) =>
        new(cert ?? _idp.CertificatePem, SamlTestIdp.IdpEntityId, SamlTestIdp.SpEntityId, SamlTestIdp.AcsUrl, "groups");

    private Action Validate(string response, SamlExpectations? expected = null) =>
        () => _sp.ValidateResponse(response, expected ?? Expected());

    public void Dispose() => _idp.Dispose();

    // ── Accepted ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_signed_assertion_yields_email_name_groups_and_replay_data()
    {
        var result = _sp.ValidateResponse(_idp.Build(new() { Groups = ["Credit", "Ops"], InResponseTo = "_req1", AssertionId = "_abc" }), Expected());

        result.Claims.Email.Should().Be("jane@acme.test");
        result.Claims.Name.Should().Be("Jane Doe");
        result.Claims.GroupList.Should().Equal("Credit", "Ops");
        result.AssertionId.Should().Be("_abc");
        result.InResponseTo.Should().Be("_req1");
        result.NotOnOrAfterUtc.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void A_signed_response_covering_an_unsigned_assertion_is_accepted() =>
        Validate(_idp.Build(new() { SignAssertion = false, SignResponse = true })).Should().NotThrow();

    [Fact]
    public void The_certificate_may_be_pasted_without_pem_headers()
    {
        var bare = string.Concat(_idp.CertificatePem.Split('\n').Where(l => !l.StartsWith("-----")));
        Validate(_idp.Build(new())).Should().NotThrow();
        Validate(_idp.Build(new()), Expected(bare)).Should().NotThrow();
    }

    // ── Forgeries ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_changed_email_breaks_the_signature()
    {
        var doc = _idp.BuildDocument(new());
        var nameId = doc.GetElementsByTagName("NameID", SamlProcessor.AssertionNs)[0]!;
        nameId.InnerText = "ceo@acme.test";

        Validate(SamlTestIdp.Encode(doc)).Should().Throw<SsoLoginException>().WithMessage("*signature*");
    }

    [Fact]
    public void Signature_wrapping_with_an_extra_assertion_is_refused()
    {
        // Keep the genuinely signed assertion, and add an attacker's assertion first in the response.
        var doc = _idp.BuildDocument(new());
        var signed = (XmlElement)doc.GetElementsByTagName("Assertion", SamlProcessor.AssertionNs)[0]!;
        var evil = (XmlElement)signed.CloneNode(deep: true);
        evil.SetAttribute("ID", "_evil");
        foreach (XmlNode sig in evil.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl).Cast<XmlNode>().ToList())
            sig.ParentNode!.RemoveChild(sig);
        evil.GetElementsByTagName("NameID", SamlProcessor.AssertionNs)[0]!.InnerText = "ceo@acme.test";
        doc.DocumentElement!.InsertBefore(evil, signed);

        Validate(SamlTestIdp.Encode(doc)).Should().Throw<SsoLoginException>().WithMessage("*exactly one assertion*");
    }

    [Fact]
    public void Signature_wrapping_that_hides_the_signed_assertion_is_refused()
    {
        // Move the signed assertion into an Extensions element and put a forged one (same ID) where the SP reads.
        var doc = _idp.BuildDocument(new());
        var signed = (XmlElement)doc.GetElementsByTagName("Assertion", SamlProcessor.AssertionNs)[0]!;
        var forged = (XmlElement)signed.CloneNode(deep: true);
        forged.GetElementsByTagName("NameID", SamlProcessor.AssertionNs)[0]!.InnerText = "ceo@acme.test";
        var extensions = doc.CreateElement("samlp", "Extensions", SamlProcessor.ProtocolNs);
        doc.DocumentElement!.ReplaceChild(forged, signed);
        extensions.AppendChild(signed);
        doc.DocumentElement.InsertBefore(extensions, forged);

        Validate(SamlTestIdp.Encode(doc)).Should().Throw<SsoLoginException>();
    }

    [Fact]
    public void A_response_signed_by_another_key_is_refused()
    {
        using var other = new SamlTestIdp();
        Validate(other.Build(new())).Should().Throw<SsoLoginException>().WithMessage("*doesn't match the configured IdP certificate*");
    }

    [Fact]
    public void An_unsigned_response_is_refused() =>
        Validate(_idp.Build(new() { SignAssertion = false })).Should().Throw<SsoLoginException>().WithMessage("*neither the assertion nor the response is signed*");

    [Fact]
    public void A_sha1_signature_is_refused() =>
        Validate(_idp.Build(new() { SignatureMethod = SignedXml.XmlDsigRSASHA1Url, DigestMethod = SignedXml.XmlDsigSHA1Url }))
            .Should().Throw<SsoLoginException>().WithMessage("*weak or unsupported*");

    // ── Wrong place, wrong time ───────────────────────────────────────────────────────

    [Fact]
    public void Another_services_audience_is_refused() =>
        Validate(_idp.Build(new() { Audience = "https://other.example/sp" })).Should().Throw<SsoLoginException>().WithMessage("*audience*");

    [Fact]
    public void Another_identity_providers_issuer_is_refused() =>
        Validate(_idp.Build(new() { Issuer = "https://evil.example/idp" })).Should().Throw<SsoLoginException>().WithMessage("*different identity provider*");

    [Fact]
    public void A_response_for_another_acs_url_is_refused() =>
        Validate(_idp.Build(new() { Recipient = "https://other.example/acs" })).Should().Throw<SsoLoginException>();

    [Fact]
    public void An_expired_assertion_is_refused() =>
        Validate(_idp.Build(new() { Now = DateTime.UtcNow.AddMinutes(-30) })).Should().Throw<SsoLoginException>().WithMessage("*expired*");

    [Fact]
    public void A_failed_status_is_refused() =>
        Validate(_idp.Build(new() { Status = "urn:oasis:names:tc:SAML:2.0:status:Requester" })).Should().Throw<SsoLoginException>().WithMessage("*sign-in failed*");

    [Fact]
    public void Dtds_and_garbage_are_refused()
    {
        var xxe = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><r>&x;</r>"));
        Validate(xxe).Should().Throw<SsoLoginException>().WithMessage("*couldn't be read*");
        Validate("not base64 at all!").Should().Throw<SsoLoginException>().WithMessage("*couldn't be read*");
    }

    // ── AuthnRequest ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AuthnRequest_uses_the_redirect_binding_with_our_id_acs_and_relay_state()
    {
        var request = _sp.BuildAuthnRequest("https://idp.acme.test/sso?tenant=1", SamlTestIdp.SpEntityId, SamlTestIdp.AcsUrl, "_req42", "relay-key");

        request.RequestId.Should().Be("_req42");
        var uri = new Uri(request.RedirectUrl);
        var query = HttpUtility.ParseQueryString(uri.Query);
        query["tenant"].Should().Be("1");
        query["RelayState"].Should().Be("relay-key");

        using var inflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(query["SAMLRequest"]!)), CompressionMode.Decompress);
        var xml = new StreamReader(inflate).ReadToEnd();
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        var root = doc.DocumentElement!;
        root.LocalName.Should().Be("AuthnRequest");
        root.GetAttribute("ID").Should().Be("_req42");
        root.GetAttribute("AssertionConsumerServiceURL").Should().Be(SamlTestIdp.AcsUrl);
        root.GetAttribute("Destination").Should().Be("https://idp.acme.test/sso?tenant=1");
        doc.GetElementsByTagName("Issuer", SamlProcessor.AssertionNs)[0]!.InnerText.Should().Be(SamlTestIdp.SpEntityId);
    }

    [Fact]
    public void Metadata_escapes_urls() =>
        _sp.BuildSpMetadata("https://x/saml/a&b", "https://x/acs?a=1&b=2").Should().Contain("a&amp;b").And.Contain("a=1&amp;b=2");
}
