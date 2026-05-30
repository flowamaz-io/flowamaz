using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services.Sso;

/// <summary>
/// SAML 2.0 SP operations using the framework XML-DSig stack (no third-party dependency).
/// <see cref="ValidateResponse"/> verifies the enveloped signature against the IdP certificate,
/// checks the audience and the NotOnOrAfter timestamp, then extracts the subject email/name.
/// </summary>
public sealed class SamlProcessor : ISamlProcessor
{
    private const string AssertionNs = "urn:oasis:names:tc:SAML:2.0:assertion";

    private readonly ILogger<SamlProcessor> _logger;

    public SamlProcessor(ILogger<SamlProcessor> logger) => _logger = logger;

    public string BuildSpMetadata(string spEntityId, string acsUrl) =>
        $"""
        <?xml version="1.0"?>
        <EntityDescriptor xmlns="urn:oasis:names:tc:SAML:2.0:metadata" entityID="{spEntityId}">
          <SPSSODescriptor AuthnRequestsSigned="false" WantAssertionsSigned="true"
              protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
            <NameIDFormat>urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress</NameIDFormat>
            <AssertionConsumerService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST"
                Location="{acsUrl}" index="0" isDefault="true"/>
          </SPSSODescriptor>
        </EntityDescriptor>
        """;

    public string BuildAuthnRequestUrl(string idpSsoUrl, string spEntityId, string? relayState)
    {
        var separator = idpSsoUrl.Contains('?') ? '&' : '?';
        var url = $"{idpSsoUrl}{separator}SPEntityID={Uri.EscapeDataString(spEntityId)}";
        if (!string.IsNullOrWhiteSpace(relayState))
            url += $"&RelayState={Uri.EscapeDataString(relayState)}";
        return url;
    }

    public SsoClaims ValidateResponse(string samlResponse, string idpCertificatePem, string expectedAudience)
    {
        var xml = DecodeResponse(samlResponse);
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);

        VerifySignature(doc, idpCertificatePem);
        VerifyConditions(doc, expectedAudience);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("a", AssertionNs);

        var email = doc.SelectSingleNode("//a:Subject/a:NameID", ns)?.InnerText?.Trim()
            ?? doc.SelectSingleNode("//a:Attribute[@Name='email']/a:AttributeValue", ns)?.InnerText?.Trim()
            ?? throw new InvalidOperationException("SAML assertion did not contain a subject email.");

        var name = doc.SelectSingleNode("//a:Attribute[@Name='name']/a:AttributeValue", ns)?.InnerText?.Trim()
            ?? doc.SelectSingleNode("//a:Attribute[@Name='displayName']/a:AttributeValue", ns)?.InnerText?.Trim();

        _logger.LogInformation("SamlProcessor.ValidateResponse ok email={Email}", email);
        return new SsoClaims(email, name);
    }

    private static string DecodeResponse(string samlResponse)
    {
        var trimmed = samlResponse.Trim();
        if (trimmed.StartsWith('<')) return trimmed;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(trimmed));
        }
        catch (FormatException)
        {
            return trimmed;
        }
    }

    private static void VerifySignature(XmlDocument doc, string certificatePem)
    {
        var signatureNode = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
        if (signatureNode.Count == 0)
            throw new InvalidOperationException("SAML response is not signed.");

        using var cert = X509Certificate2.CreateFromPem(NormalisePem(certificatePem));
        var signedXml = new SignedXml(doc);
        signedXml.LoadXml((XmlElement)signatureNode[0]!);

        using var key = cert.GetRSAPublicKey()
            ?? throw new InvalidOperationException("IdP certificate has no RSA public key.");
        if (!signedXml.CheckSignature(key))
            throw new InvalidOperationException("SAML signature validation failed — the assertion is not trusted.");
    }

    private static void VerifyConditions(XmlDocument doc, string expectedAudience)
    {
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("a", AssertionNs);

        var notOnOrAfter = doc.SelectSingleNode("//a:Conditions/@NotOnOrAfter", ns)?.Value;
        if (DateTimeOffset.TryParse(notOnOrAfter, out var expiry) && expiry < DateTimeOffset.UtcNow)
            throw new InvalidOperationException("SAML assertion has expired (NotOnOrAfter is in the past).");

        if (!string.IsNullOrEmpty(expectedAudience))
        {
            var audience = doc.SelectSingleNode("//a:AudienceRestriction/a:Audience", ns)?.InnerText?.Trim();
            if (audience is not null && !string.Equals(audience, expectedAudience, StringComparison.Ordinal))
                throw new InvalidOperationException("SAML audience does not match this service provider.");
        }
    }

    private static string NormalisePem(string pem) =>
        pem.Contains("BEGIN CERTIFICATE")
            ? pem
            : $"-----BEGIN CERTIFICATE-----\n{pem.Trim()}\n-----END CERTIFICATE-----";
}
