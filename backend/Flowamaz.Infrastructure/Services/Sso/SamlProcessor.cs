using System.IO.Compression;
using System.Net.Mail;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services.Sso;

/// <summary>
/// SAML 2.0 service provider (framework XML-DSig, no third-party dependency).
/// <para>
/// A response is accepted only if: it is a successful <c>samlp:Response</c> holding exactly one (unencrypted)
/// assertion; the assertion — or the response that contains it — carries an RSA-SHA256+ signature by the IdP
/// certificate whose single reference points at that very element (by a unique ID), so claims are only ever read
/// from signed XML (no signature-wrapping); the issuer is the configured IdP; the audience is this SP; the bearer
/// subject confirmation is addressed to this ACS URL; and the time window is valid (3 minutes' clock skew).
/// DTDs and external entities are refused.
/// </para>
/// </summary>
public sealed class SamlProcessor : ISamlProcessor
{
    public const string ProtocolNs = "urn:oasis:names:tc:SAML:2.0:protocol";
    public const string AssertionNs = "urn:oasis:names:tc:SAML:2.0:assertion";
    private const string StatusSuccess = "urn:oasis:names:tc:SAML:2.0:status:Success";
    private const string BearerMethod = "urn:oasis:names:tc:SAML:2.0:cm:bearer";
    private const string EmailNameIdFormat = "urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress";
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(3);

    private static readonly HashSet<string> AllowedSignatureMethods =
    [
        "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256",
        "http://www.w3.org/2001/04/xmldsig-more#rsa-sha384",
        "http://www.w3.org/2001/04/xmldsig-more#rsa-sha512",
    ];

    private static readonly HashSet<string> AllowedDigestMethods =
    [
        "http://www.w3.org/2001/04/xmlenc#sha256",
        "http://www.w3.org/2001/04/xmldsig-more#sha384",
        "http://www.w3.org/2001/04/xmlenc#sha512",
    ];

    private static readonly HashSet<string> AllowedTransforms =
    [
        SignedXml.XmlDsigEnvelopedSignatureTransformUrl,
        SignedXml.XmlDsigExcC14NTransformUrl,
    ];

    private static readonly string[] EmailAttributes =
    [
        "email", "mail", "emailaddress", "Email",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress",
        "urn:oid:0.9.2342.19200300.100.1.3",
    ];

    private static readonly string[] NameAttributes =
    [
        "displayName", "name", "http://schemas.microsoft.com/identity/claims/displayname", "urn:oid:2.16.840.1.113730.3.1.241",
    ];

    private static readonly string[] KnownGroupAttributes =
    [
        "groups", "memberOf", "http://schemas.microsoft.com/ws/2008/06/identity/claims/groups", "http://schemas.xmlsoap.org/claims/Group",
    ];

    private readonly ILogger<SamlProcessor> _logger;
    private readonly TimeProvider _clock;

    public SamlProcessor(ILogger<SamlProcessor> logger, TimeProvider? clock = null)
    {
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public string BuildSpMetadata(string spEntityId, string acsUrl) =>
        $"""
        <?xml version="1.0"?>
        <EntityDescriptor xmlns="urn:oasis:names:tc:SAML:2.0:metadata" entityID="{SecurityElement.Escape(spEntityId)}">
          <SPSSODescriptor AuthnRequestsSigned="false" WantAssertionsSigned="true"
              protocolSupportEnumeration="{ProtocolNs}">
            <NameIDFormat>{EmailNameIdFormat}</NameIDFormat>
            <AssertionConsumerService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST"
                Location="{SecurityElement.Escape(acsUrl)}" index="0" isDefault="true"/>
          </SPSSODescriptor>
        </EntityDescriptor>
        """;

    public SamlAuthnRequest BuildAuthnRequest(string idpSsoUrl, string spEntityId, string acsUrl, string requestId, string relayState)
    {
        _logger.LogDebug("SamlProcessor.BuildAuthnRequest enter");
        var xml =
            $"<samlp:AuthnRequest xmlns:samlp=\"{ProtocolNs}\" xmlns:saml=\"{AssertionNs}\" ID=\"{SecurityElement.Escape(requestId)}\" Version=\"2.0\" " +
            $"IssueInstant=\"{Now:yyyy-MM-ddTHH:mm:ssZ}\" Destination=\"{SecurityElement.Escape(idpSsoUrl)}\" " +
            $"AssertionConsumerServiceURL=\"{SecurityElement.Escape(acsUrl)}\" " +
            "ProtocolBinding=\"urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST\">" +
            $"<saml:Issuer>{SecurityElement.Escape(spEntityId)}</saml:Issuer>" +
            $"<samlp:NameIDPolicy Format=\"{EmailNameIdFormat}\" AllowCreate=\"true\"/>" +
            "</samlp:AuthnRequest>";

        // HTTP-Redirect binding: raw DEFLATE, then base64, then URL-encode.
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(Encoding.UTF8.GetBytes(xml));
        var encoded = Uri.EscapeDataString(Convert.ToBase64String(buffer.ToArray()));

        var separator = idpSsoUrl.Contains('?') ? '&' : '?';
        var url = $"{idpSsoUrl}{separator}SAMLRequest={encoded}&RelayState={Uri.EscapeDataString(relayState)}";
        _logger.LogDebug("SamlProcessor.BuildAuthnRequest exit request={RequestId}", requestId);
        return new SamlAuthnRequest(url, requestId);
    }

    public SamlAssertion ValidateResponse(string samlResponse, SamlExpectations expected)
    {
        _logger.LogDebug("SamlProcessor.ValidateResponse enter");
        try
        {
            var doc = Load(samlResponse);
            var ns = Namespaces(doc);
            var response = doc.DocumentElement;
            if (response is null || response.LocalName != "Response" || response.NamespaceURI != ProtocolNs)
                throw SsoLoginException.Rejected("the identity provider didn't send a SAML response.");

            var status = response.SelectSingleNode("p:Status/p:StatusCode/@Value", ns)?.Value;
            if (status != StatusSuccess)
                throw SsoLoginException.Rejected("the identity provider reported that sign-in failed.");

            if (doc.GetElementsByTagName("EncryptedAssertion", AssertionNs).Count > 0)
                throw SsoLoginException.Rejected("the assertion is encrypted, which Flowamaz doesn't support yet. Turn off assertion encryption for the Flowamaz app in your IdP.");

            // Exactly one assertion anywhere in the document, and it is a direct child of the response.
            var all = doc.GetElementsByTagName("Assertion", AssertionNs);
            var assertion = response.SelectSingleNode("a:Assertion", ns) as XmlElement;
            if (all.Count != 1 || assertion is null)
                throw SsoLoginException.Rejected("the response must contain exactly one assertion.");

            var destination = response.GetAttribute("Destination");
            if (destination.Length > 0 && !SameUrl(destination, expected.AcsUrl))
                throw SsoLoginException.Rejected("the response is addressed to a different service.");

            VerifySignature(doc, response, assertion, expected.IdpCertificatePem, ns);

            // From here on, claims come only from the verified assertion element.
            var issuer = assertion.SelectSingleNode("a:Issuer", ns)?.InnerText.Trim();
            if (!string.Equals(issuer, expected.IdpEntityId.Trim(), StringComparison.Ordinal))
                throw SsoLoginException.Rejected("the assertion comes from a different identity provider than the one configured.");

            var now = Now;
            var (confirmationExpiry, inResponseTo) = CheckSubjectConfirmation(assertion, expected.AcsUrl, now, ns);
            var conditionsExpiry = CheckConditions(assertion, expected.SpEntityId, now, ns);

            // The replay cache keys on this id, so it must be present and well-formed (xs:ID is an NCName).
            var assertionId = assertion.GetAttribute("ID");
            if (!IsNcName(assertionId))
                throw SsoLoginException.Rejected("the assertion has no valid ID.");
            var claims = ReadClaims(assertion, expected.GroupsAttribute, issuer!, ns);
            var expiry = conditionsExpiry is { } c && c < confirmationExpiry ? c : confirmationExpiry;

            _logger.LogInformation("SamlProcessor.ValidateResponse ok assertion={AssertionId} groups={GroupCount}", assertionId, claims.GroupList.Count);
            return new SamlAssertion(claims, assertionId, expiry, string.IsNullOrEmpty(inResponseTo) ? null : inResponseTo);
        }
        catch (SsoLoginException ex)
        {
            _logger.LogWarning("SamlProcessor.ValidateResponse rejected code={Code} reason={Reason}", ex.ErrorCode, ex.Message);
            throw;
        }
        catch (Exception ex) when (ex is XmlException or FormatException or CryptographicException)
        {
            _logger.LogWarning("SamlProcessor.ValidateResponse unreadable: {Error}", ex.GetType().Name);
            throw SsoLoginException.Rejected("the response couldn't be read.");
        }
    }

    // ── Parsing ─────────────────────────────────────────────────────────────────────────

    private static XmlDocument Load(string samlResponse)
    {
        // HTTP-POST binding: base64 of the XML. Anything else is refused.
        var xml = Encoding.UTF8.GetString(Convert.FromBase64String(samlResponse.Trim()));
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1_000_000,
        };
        var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        doc.Load(reader);
        return doc;
    }

    private static XmlNamespaceManager Namespaces(XmlDocument doc)
    {
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("p", ProtocolNs);
        ns.AddNamespace("a", AssertionNs);
        ns.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
        return ns;
    }

    // ── Signature ───────────────────────────────────────────────────────────────────────

    private static void VerifySignature(XmlDocument doc, XmlElement response, XmlElement assertion, string certificatePem, XmlNamespaceManager ns)
    {
        using var cert = X509Certificate2.CreateFromPem(NormalisePem(certificatePem));
        using var key = cert.GetRSAPublicKey()
            ?? throw SsoLoginException.Rejected("the configured IdP certificate has no RSA key.");

        // The assertion's own signature, else the response's (which then covers the assertion inside it).
        if (assertion.SelectSingleNode("ds:Signature", ns) is XmlElement assertionSig)
        {
            VerifyElement(doc, assertion, assertionSig, key);
            return;
        }
        if (response.SelectSingleNode("ds:Signature", ns) is XmlElement responseSig)
        {
            VerifyElement(doc, response, responseSig, key);
            return;
        }
        throw SsoLoginException.Rejected("neither the assertion nor the response is signed.");
    }

    private static void VerifyElement(XmlDocument doc, XmlElement signedElement, XmlElement signature, RSA key)
    {
        var id = signedElement.GetAttribute("ID");
        if (string.IsNullOrEmpty(id) || CountElementsWithId(doc, id) != 1)
            throw SsoLoginException.Rejected("the signed element's ID is missing or not unique.");

        var signedXml = new SignedXml(doc);
        signedXml.LoadXml(signature);

        if (!AllowedSignatureMethods.Contains(signedXml.SignatureMethod ?? ""))
            throw SsoLoginException.Rejected("the signature uses a weak or unsupported algorithm (use RSA-SHA256 or stronger).");
        if (signedXml.SignedInfo?.References is not { Count: 1 } references || references[0] is not Reference reference)
            throw SsoLoginException.Rejected("the signature must have exactly one reference.");
        if (reference.Uri != "#" + id)
            throw SsoLoginException.Rejected("the signature doesn't cover the assertion it accompanies.");
        if (!AllowedDigestMethods.Contains(reference.DigestMethod ?? ""))
            throw SsoLoginException.Rejected("the signature uses a weak or unsupported digest (use SHA-256 or stronger).");
        foreach (Transform transform in reference.TransformChain)
            if (!AllowedTransforms.Contains(transform.Algorithm ?? ""))
                throw SsoLoginException.Rejected("the signature uses an unsupported transform.");

        if (!signedXml.CheckSignature(key))
            throw SsoLoginException.Rejected("the signature doesn't match the configured IdP certificate.");
    }

    private static int CountElementsWithId(XmlDocument doc, string id)
    {
        var count = 0;
        foreach (XmlElement element in doc.GetElementsByTagName("*"))
            if (element.GetAttribute("ID") == id || element.GetAttribute("Id") == id || element.GetAttribute("id") == id)
                count++;
        return count;
    }

    // ── Subject, conditions, claims ─────────────────────────────────────────────────────

    private static (DateTime Expiry, string? InResponseTo) CheckSubjectConfirmation(
        XmlElement assertion, string acsUrl, DateTime now, XmlNamespaceManager ns)
    {
        var confirmations = assertion.SelectNodes("a:Subject/a:SubjectConfirmation", ns);
        if (confirmations is null) throw SsoLoginException.Rejected("the assertion has no subject confirmation.");

        foreach (XmlElement confirmation in confirmations)
        {
            if (confirmation.GetAttribute("Method") != BearerMethod) continue;
            if (confirmation.SelectSingleNode("a:SubjectConfirmationData", ns) is not XmlElement data) continue;
            if (!SameUrl(data.GetAttribute("Recipient"), acsUrl)) continue;
            if (!TryParseTime(data.GetAttribute("NotOnOrAfter"), out var notOnOrAfter)) continue;
            if (now - ClockSkew >= notOnOrAfter)
                throw SsoLoginException.Rejected("the assertion has expired.");
            if (TryParseTime(data.GetAttribute("NotBefore"), out var notBefore) && now + ClockSkew < notBefore)
                throw SsoLoginException.Rejected("the assertion isn't valid yet (check the IdP server's clock).");
            var inResponseTo = data.GetAttribute("InResponseTo");
            return (notOnOrAfter, inResponseTo.Length == 0 ? null : inResponseTo);
        }
        throw SsoLoginException.Rejected("the assertion isn't addressed to this service (bearer confirmation with this ACS URL and an expiry is required).");
    }

    private static DateTime? CheckConditions(XmlElement assertion, string spEntityId, DateTime now, XmlNamespaceManager ns)
    {
        if (assertion.SelectSingleNode("a:Conditions", ns) is not XmlElement conditions)
            throw SsoLoginException.Rejected("the assertion has no conditions (audience is required).");

        if (TryParseTime(conditions.GetAttribute("NotBefore"), out var notBefore) && now + ClockSkew < notBefore)
            throw SsoLoginException.Rejected("the assertion isn't valid yet (check the IdP server's clock).");
        DateTime? notOnOrAfter = null;
        if (TryParseTime(conditions.GetAttribute("NotOnOrAfter"), out var end))
        {
            if (now - ClockSkew >= end) throw SsoLoginException.Rejected("the assertion has expired.");
            notOnOrAfter = end;
        }

        var audiences = conditions.SelectNodes("a:AudienceRestriction/a:Audience", ns);
        var matched = false;
        if (audiences is not null)
            foreach (XmlNode audience in audiences)
                if (string.Equals(audience.InnerText.Trim(), spEntityId, StringComparison.Ordinal)) matched = true;
        if (!matched)
            throw SsoLoginException.Rejected("the assertion is meant for a different service (audience mismatch).");
        return notOnOrAfter;
    }

    private static SsoClaims ReadClaims(XmlElement assertion, string groupsAttribute, string issuer, XmlNamespaceManager ns)
    {
        var attributes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var nodes = assertion.SelectNodes("a:AttributeStatement/a:Attribute", ns);
        if (nodes is not null)
            foreach (XmlElement attribute in nodes)
            {
                var values = attribute.SelectNodes("a:AttributeValue", ns)!.Cast<XmlNode>()
                    .Select(v => v.InnerText.Trim()).Where(v => v.Length > 0).ToList();
                foreach (var attributeName in new[] { attribute.GetAttribute("Name"), attribute.GetAttribute("FriendlyName") }.Where(n => n.Length > 0))
                {
                    if (!attributes.TryGetValue(attributeName, out var list)) attributes[attributeName] = list = [];
                    list.AddRange(values);
                }
            }

        string? First(IEnumerable<string> names) =>
            names.Select(n => attributes.TryGetValue(n, out var v) ? v.FirstOrDefault() : null).FirstOrDefault(v => v is not null);

        var nameIdElement = assertion.SelectSingleNode("a:Subject/a:NameID", ns) as XmlElement;
        var nameId = nameIdElement?.InnerText.Trim();
        var email = IsEmail(nameId) ? nameId : First(EmailAttributes);
        if (!IsEmail(email))
            throw SsoLoginException.Rejected("the assertion has no email address. Send the email as NameID or an 'email' attribute.");

        var name = First(NameAttributes);
        if (name is null)
        {
            var given = First(["givenName", "firstName", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname"]);
            var surname = First(["sn", "surname", "lastName", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname"]);
            name = string.Join(' ', new[] { given, surname }.Where(p => !string.IsNullOrWhiteSpace(p))).NullIfEmpty();
        }

        var groupNames = new[] { groupsAttribute }.Concat(KnownGroupAttributes).Distinct(StringComparer.OrdinalIgnoreCase);
        var groups = groupNames.SelectMany(n => attributes.TryGetValue(n, out var v) ? v : [])
            .Distinct(StringComparer.Ordinal).ToList();

        // A persistent NameID is the IdP's stable id for the person; the account is linked to it. Transient and
        // email NameIDs aren't stable identities, so they don't link.
        var subject = nameIdElement?.GetAttribute("Format") == PersistentNameIdFormat && !string.IsNullOrEmpty(nameId)
            ? $"saml|{issuer}|{nameId}" : null;

        return new SsoClaims(email!.ToLowerInvariant(), name, groups, subject);
    }

    private const string PersistentNameIdFormat = "urn:oasis:names:tc:SAML:2.0:nameid-format:persistent";

    internal static bool IsNcName(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 256) return false;
        try { XmlConvert.VerifyNCName(value); return true; }
        catch (XmlException) { return false; }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────

    private static bool IsEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254) return false;
        try { return new MailAddress(value).Address == value; }
        catch (FormatException) { return false; }
    }

    private static bool TryParseTime(string value, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrEmpty(value)) return false;
        if (!DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)) return false;
        utc = parsed.UtcDateTime;
        return true;
    }

    private static bool SameUrl(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static string NormalisePem(string pem) =>
        pem.Contains("BEGIN CERTIFICATE")
            ? pem
            : $"-----BEGIN CERTIFICATE-----\n{pem.Trim()}\n-----END CERTIFICATE-----";
}

internal static class SamlStringExtensions
{
    public static string? NullIfEmpty(this string value) => value.Length == 0 ? null : value;
}
