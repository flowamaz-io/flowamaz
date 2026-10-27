using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using FluentAssertions;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Services.Sso;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace Flowamaz.Tests.Unit.Auth.Sso;

/// <summary>
/// OIDC client against a fake IdP that signs real id_tokens: discovery (issuer must match), PKCE + nonce in the
/// authorize URL, and full id_token validation (signature, issuer, audience, expiry, nonce, email_verified).
/// </summary>
public sealed class OidcClientTests : IDisposable
{
    private const string Issuer = "https://idp.acme.test";
    private const string ClientId = "flowamaz-client";
    private readonly RSA _key = RSA.Create(2048);
    private readonly RsaSecurityKey _signingKey;
    private readonly FakeIdp _http = new();

    public OidcClientTests()
    {
        OidcClient.ClearCaches();
        _signingKey = new RsaSecurityKey(_key) { KeyId = "k1" };
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(_signingKey);
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;
        _http.Routes["/.well-known/openid-configuration"] = () => JsonSerializer.Serialize(new
        {
            issuer = Issuer,
            authorization_endpoint = $"{Issuer}/oauth2/authorize",
            token_endpoint = $"{Issuer}/oauth2/token",
            jwks_uri = $"{Issuer}/oauth2/keys",
        });
        _http.Routes["/oauth2/keys"] = () => JsonSerializer.Serialize(new { keys = new[] { jwk } });
    }

    public void Dispose() => _key.Dispose();

    private OidcClient Client()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OidcClient.HttpClientName)).Returns(() => new HttpClient(_http));
        return new OidcClient(factory.Object, NullLogger<OidcClient>.Instance);
    }

    private string IdToken(Action<Dictionary<string, object>>? change = null, SecurityKey? key = null, string issuer = Issuer, string audience = ClientId,
        DateTime? expires = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = "u1", ["email"] = "Jane@Acme.test", ["email_verified"] = true, ["name"] = "Jane Doe", ["nonce"] = "n-1",
            ["groups"] = new[] { "Credit", "Ops" },
        };
        change?.Invoke(claims);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(key ?? _signingKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private void TokenEndpointReturns(string idToken) =>
        _http.Routes["/oauth2/token"] = () => JsonSerializer.Serialize(new { access_token = "at", id_token = idToken, token_type = "Bearer" });

    private async Task<SsoClaims> Exchange()
    {
        var client = Client();
        var discovery = await client.DiscoverAsync(Issuer);
        return await client.ExchangeCodeAsync(discovery, ClientId, "secret", "https://app/cb", "code-1", "verifier-1", "n-1", "groups");
    }

    [Fact]
    public async Task A_valid_id_token_gives_email_name_and_groups_and_the_code_exchange_carries_pkce()
    {
        TokenEndpointReturns(IdToken());

        var claims = await Exchange();

        claims.Email.Should().Be("jane@acme.test");
        claims.Name.Should().Be("Jane Doe");
        claims.GroupList.Should().Equal("Credit", "Ops");
        claims.Subject.Should().Be($"oidc|{Issuer}|u1");
        var form = HttpUtility.ParseQueryString(_http.LastTokenRequestBody!);
        form["code_verifier"].Should().Be("verifier-1");
        form["grant_type"].Should().Be("authorization_code");
    }

    [Fact]
    public async Task Authorize_url_comes_from_discovery_and_carries_state_nonce_and_s256_challenge()
    {
        var client = Client();
        var discovery = await client.DiscoverAsync(Issuer);
        var url = new Uri(client.BuildAuthorizeUrl(discovery, ClientId, "https://app/cb", ["email", "profile"], "st", "nn", "ch"));

        url.GetLeftPart(UriPartial.Path).Should().Be($"{Issuer}/oauth2/authorize");
        var q = HttpUtility.ParseQueryString(url.Query);
        q["state"].Should().Be("st");
        q["nonce"].Should().Be("nn");
        q["code_challenge"].Should().Be("ch");
        q["code_challenge_method"].Should().Be("S256");
        q["scope"].Should().Be("openid email profile");
    }

    [Fact]
    public async Task A_token_signed_by_another_key_is_refused()
    {
        using var other = RSA.Create(2048);
        TokenEndpointReturns(IdToken(key: new RsaSecurityKey(other) { KeyId = "k1" }));
        await FluentActions.Awaiting(Exchange).Should().ThrowAsync<SsoLoginException>().WithMessage("*signature*");
    }

    [Fact]
    public async Task A_token_for_another_client_is_refused()
    {
        TokenEndpointReturns(IdToken(audience: "someone-else"));
        await FluentActions.Awaiting(Exchange).Should().ThrowAsync<SsoLoginException>().WithMessage("*different app*");
    }

    [Fact]
    public async Task A_token_from_another_issuer_is_refused()
    {
        TokenEndpointReturns(IdToken(issuer: "https://evil.test"));
        await FluentActions.Awaiting(Exchange).Should().ThrowAsync<SsoLoginException>().WithMessage("*different issuer*");
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        TokenEndpointReturns(IdToken(expires: DateTime.UtcNow.AddMinutes(-10)));
        await FluentActions.Awaiting(Exchange).Should().ThrowAsync<SsoLoginException>();
    }

    [Fact]
    public async Task A_token_with_another_nonce_is_refused()
    {
        TokenEndpointReturns(IdToken(c => c["nonce"] = "other"));
        await FluentActions.Awaiting(Exchange).Should().ThrowAsync<SsoLoginException>().WithMessage("*nonce*");
    }

    [Fact]
    public async Task An_unsigned_token_is_refused()
    {
        var header = Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Base64Url(JsonSerializer.Serialize(new
        {
            iss = Issuer, aud = ClientId, email = "ceo@acme.test", nonce = "n-1",
            exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(),
        }));
        TokenEndpointReturns($"{header}.{payload}.");
        await FluentActions.Awaiting(Exchange).Should().ThrowAsync<SsoLoginException>();
    }

    [Fact]
    public async Task An_unverified_email_is_refused()
    {
        TokenEndpointReturns(IdToken(c => c["email_verified"] = false));
        await FluentActions.Awaiting(Exchange).Should().ThrowAsync<SsoLoginException>().Where(e => e.ErrorCode == "SSO_EMAIL_UNVERIFIED");
    }

    [Fact]
    public async Task An_email_the_idp_doesnt_mark_verified_is_refused()
    {
        TokenEndpointReturns(IdToken(c => c.Remove("email_verified")));
        await FluentActions.Awaiting(Exchange).Should().ThrowAsync<SsoLoginException>().Where(e => e.ErrorCode == "SSO_EMAIL_UNVERIFIED");
    }

    [Fact]
    public async Task Entra_verified_email_claim_is_accepted()
    {
        TokenEndpointReturns(IdToken(c => { c.Remove("email_verified"); c["xms_edov"] = true; }));
        (await Exchange()).Email.Should().Be("jane@acme.test");
    }

    [Fact]
    public async Task A_upn_is_never_used_as_the_email()
    {
        TokenEndpointReturns(IdToken(c => { c.Remove("email"); c["upn"] = "ceo@acme.test"; }));
        await FluentActions.Awaiting(Exchange).Should().ThrowAsync<SsoLoginException>().WithMessage("*no email*");
    }

    [Theory]
    [InlineData("http://idp.acme.test")]
    [InlineData("https://user:pw@idp.acme.test")]
    public async Task An_insecure_issuer_url_is_refused(string issuer) =>
        await FluentActions.Awaiting(() => Client().DiscoverAsync(issuer)).Should().ThrowAsync<SsoLoginException>().WithMessage("*https*");

    [Fact]
    public async Task Discovery_pointing_at_http_endpoints_is_refused()
    {
        _http.Routes["/.well-known/openid-configuration"] = () => JsonSerializer.Serialize(new
        {
            issuer = Issuer, authorization_endpoint = $"{Issuer}/a", token_endpoint = "http://169.254.169.254/latest", jwks_uri = $"{Issuer}/k",
        });
        await FluentActions.Awaiting(() => Client().DiscoverAsync(Issuer)).Should().ThrowAsync<SsoLoginException>().WithMessage("*token endpoint*https*");
    }

    [Theory]
    [InlineData("http://127.0.0.1:9/")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://10.0.0.1:9/")]
    public async Task The_oidc_http_handler_refuses_private_addresses(string url)
    {
        using var client = new HttpClient(OidcClient.CreateHandler(allowPrivateNetworks: false)) { Timeout = TimeSpan.FromSeconds(5) };
        var ex = await FluentActions.Awaiting(() => client.GetAsync(url)).Should().ThrowAsync<HttpRequestException>();
        ex.Which.Message.Should().Contain("private or internal");
    }

    [Fact]
    public void The_oidc_http_handler_follows_no_redirects() =>
        OidcClient.CreateHandler(allowPrivateNetworks: false).AllowAutoRedirect.Should().BeFalse();

    [Fact]
    public async Task Discovery_with_a_different_issuer_is_refused()
    {
        _http.Routes["/.well-known/openid-configuration"] = () => JsonSerializer.Serialize(new
        {
            issuer = "https://evil.test", authorization_endpoint = "a", token_endpoint = "t", jwks_uri = "j",
        });
        await FluentActions.Awaiting(() => Client().DiscoverAsync(Issuer)).Should().ThrowAsync<SsoLoginException>().WithMessage("*doesn't match*");
    }

    [Fact]
    public async Task A_refused_code_exchange_reports_the_idp_error_code_only()
    {
        _http.Routes["/oauth2/token"] = () => throw new HttpRequestException("400");
        _http.StatusFor["/oauth2/token"] = HttpStatusCode.BadRequest;
        _http.Routes["/oauth2/token"] = () => "{\"error\":\"invalid_grant\",\"error_description\":\"secret xyz\"}";
        var ex = await FluentActions.Awaiting(Exchange).Should().ThrowAsync<SsoLoginException>();
        ex.Which.Message.Should().Contain("invalid_grant").And.NotContain("secret xyz");
    }

    private static string Base64Url(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class FakeIdp : HttpMessageHandler
    {
        public Dictionary<string, Func<string>> Routes { get; } = [];
        public Dictionary<string, HttpStatusCode> StatusFor { get; } = [];
        public string? LastTokenRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/oauth2/token" && request.Content is not null) LastTokenRequestBody = await request.Content.ReadAsStringAsync(ct);
            if (!Routes.TryGetValue(path, out var body)) return new HttpResponseMessage(HttpStatusCode.NotFound);
            return new HttpResponseMessage(StatusFor.GetValueOrDefault(path, HttpStatusCode.OK))
            {
                Content = new StringContent(body(), Encoding.UTF8, "application/json"),
            };
        }
    }
}
