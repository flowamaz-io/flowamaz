using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace Flowamaz.Tests.Unit.Auth.Sso;

/// <summary>
/// A tiny in-test SAML identity provider: a self-signed RSA certificate and builders for signed responses, so the
/// service provider is tested against real XML-DSig (and real forgeries), not mocks.
/// </summary>
public sealed class SamlTestIdp : IDisposable
{
    public const string IdpEntityId = "https://idp.acme.test/metadata";
    public const string SpEntityId = "https://app.flowamaz.io/saml/acme";
    public const string AcsUrl = "https://app.flowamaz.io/api/v1/saml/acme/acs";

    private readonly RSA _key = RSA.Create(2048);
    public X509Certificate2 Certificate { get; }

    public SamlTestIdp()
    {
        var request = new CertificateRequest("CN=Test IdP", _key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    public string CertificatePem => Certificate.ExportCertificatePem();

    public sealed record Options
    {
        public string Email { get; init; } = "jane@acme.test";
        public string Issuer { get; init; } = IdpEntityId;
        public string Audience { get; init; } = SpEntityId;
        public string Recipient { get; init; } = AcsUrl;
        public string? InResponseTo { get; init; }
        public DateTime Now { get; init; } = DateTime.UtcNow;
        public TimeSpan ValidFor { get; init; } = TimeSpan.FromMinutes(5);
        public string[] Groups { get; init; } = [];
        public string Status { get; init; } = "urn:oasis:names:tc:SAML:2.0:status:Success";
        public bool SignAssertion { get; init; } = true;
        public bool SignResponse { get; init; }
        public string SignatureMethod { get; init; } = SignedXml.XmlDsigRSASHA256Url;
        public string DigestMethod { get; init; } = SignedXml.XmlDsigSHA256Url;
        public string AssertionId { get; init; } = "_a" + Guid.NewGuid().ToString("N");
        public string NameIdFormat { get; init; } = "urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress";
        public string? NameId { get; init; }
    }

    public XmlDocument BuildDocument(Options o)
    {
        string T(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var inResponse = o.InResponseTo is null ? "" : $" InResponseTo=\"{o.InResponseTo}\"";
        var groups = o.Groups.Length == 0 ? "" :
            "<saml:Attribute Name=\"groups\">" + string.Concat(o.Groups.Select(g => $"<saml:AttributeValue>{g}</saml:AttributeValue>")) + "</saml:Attribute>";
        var xml =
            $"<samlp:Response xmlns:samlp=\"urn:oasis:names:tc:SAML:2.0:protocol\" xmlns:saml=\"urn:oasis:names:tc:SAML:2.0:assertion\" " +
            $"ID=\"_r{Guid.NewGuid():N}\" Version=\"2.0\" IssueInstant=\"{T(o.Now)}\" Destination=\"{o.Recipient}\"{inResponse}>" +
            $"<saml:Issuer>{o.Issuer}</saml:Issuer>" +
            $"<samlp:Status><samlp:StatusCode Value=\"{o.Status}\"/></samlp:Status>" +
            $"<saml:Assertion ID=\"{o.AssertionId}\" Version=\"2.0\" IssueInstant=\"{T(o.Now)}\">" +
            $"<saml:Issuer>{o.Issuer}</saml:Issuer>" +
            $"<saml:Subject><saml:NameID Format=\"{o.NameIdFormat}\">{o.NameId ?? o.Email}</saml:NameID>" +
            $"<saml:SubjectConfirmation Method=\"urn:oasis:names:tc:SAML:2.0:cm:bearer\">" +
            $"<saml:SubjectConfirmationData Recipient=\"{o.Recipient}\" NotOnOrAfter=\"{T(o.Now + o.ValidFor)}\"{inResponse}/>" +
            "</saml:SubjectConfirmation></saml:Subject>" +
            $"<saml:Conditions NotBefore=\"{T(o.Now.AddMinutes(-1))}\" NotOnOrAfter=\"{T(o.Now + o.ValidFor)}\">" +
            $"<saml:AudienceRestriction><saml:Audience>{o.Audience}</saml:Audience></saml:AudienceRestriction></saml:Conditions>" +
            "<saml:AttributeStatement>" +
            "<saml:Attribute Name=\"displayName\"><saml:AttributeValue>Jane Doe</saml:AttributeValue></saml:Attribute>" +
            $"<saml:Attribute Name=\"email\"><saml:AttributeValue>{o.Email}</saml:AttributeValue></saml:Attribute>" +
            groups + "</saml:AttributeStatement>" +
            "</saml:Assertion></samlp:Response>";

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("samlp", "urn:oasis:names:tc:SAML:2.0:protocol");
        ns.AddNamespace("saml", "urn:oasis:names:tc:SAML:2.0:assertion");

        if (o.SignAssertion)
        {
            var assertion = (XmlElement)doc.SelectSingleNode("//saml:Assertion", ns)!;
            Sign(doc, assertion, insertAfter: (XmlElement)assertion.SelectSingleNode("saml:Issuer", ns)!, o);
        }
        if (o.SignResponse)
        {
            var response = doc.DocumentElement!;
            Sign(doc, response, insertAfter: (XmlElement)response.SelectSingleNode("saml:Issuer", ns)!, o);
        }
        return doc;
    }

    public string Build(Options o) => Encode(BuildDocument(o));

    public static string Encode(XmlDocument doc) => Convert.ToBase64String(Encoding.UTF8.GetBytes(doc.OuterXml));

    private void Sign(XmlDocument doc, XmlElement element, XmlElement insertAfter, Options o)
    {
        var signed = new SignedXml(element) { SigningKey = _key };
        signed.SignedInfo!.SignatureMethod = o.SignatureMethod;
        signed.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
        var reference = new Reference("#" + element.GetAttribute("ID")) { DigestMethod = o.DigestMethod };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigExcC14NTransform());
        signed.AddReference(reference);
        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(Certificate));
        signed.KeyInfo = keyInfo;
        signed.ComputeSignature();
        element.InsertAfter(doc.ImportNode(signed.GetXml(), true), insertAfter);
    }

    public void Dispose()
    {
        Certificate.Dispose();
        _key.Dispose();
    }
}
