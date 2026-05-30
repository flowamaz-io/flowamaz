using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Flowamaz.Tests.Integration.Common;

namespace Flowamaz.Tests.Integration.Phase6;

/// <summary>
/// Webhook triggers end-to-end over the real API: create endpoint → POST signed payload to the
/// public receive endpoint → instance created. Tampered signature is rejected; a repeated
/// idempotency key returns the same instance.
/// </summary>
[Collection("api")]
public class Phase6WebhookTests : ApiTestBase
{
    public Phase6WebhookTests(IntegrationApiFixture fixture) : base(fixture) { }

    private const string Yaml = """
        workflow: { id: hook, version: v1, name: Hook }
        nodes:
          - { id: start, type: Trigger }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: done }
        """;

    private static string Sign(string secret, string body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private async Task<(Guid EndpointId, string Secret)> SetupWebhookAsync(Owner owner, string wsSlug)
    {
        var ws = await CreateWorkspaceAsync(owner.Client, "Hooks", wsSlug);
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Hook", slug = "hook-wf", yamlContent = Yaml, createdByMethod = "NaturalLanguage" });
        create.EnsureSuccessStatusCode();
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();
        (await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null)).EnsureSuccessStatusCode();

        var hook = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/webhooks",
            new { workflowDefinitionId = workflowId, description = "ci" });
        hook.StatusCode.Should().Be(HttpStatusCode.OK, await hook.Content.ReadAsStringAsync());
        var data = await DataAsync(hook);
        return (data.GetProperty("endpointId").GetGuid(), data.GetProperty("secret").GetString()!);
    }

    private static HttpRequestMessage Receive(Guid endpointId, string body, string? signature, string? idempotencyKey = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/webhooks/{endpointId}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (signature is not null) req.Headers.Add("X-Flowamaz-Signature", signature);
        if (idempotencyKey is not null) req.Headers.Add("X-Idempotency-Key", idempotencyKey);
        return req;
    }

    [Fact]
    public async Task Correct_signature_triggers_an_instance()
    {
        var owner = await RegisterOwnerAsync("p6-hook1");
        var (endpointId, secret) = await SetupWebhookAsync(owner, "hooks1");
        const string body = "{\"event\":\"order.created\"}";

        var resp = await Fixture.NewClient().SendAsync(Receive(endpointId, body, Sign(secret, body)));

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted, await resp.Content.ReadAsStringAsync());
        (await DataAsync(resp)).GetProperty("instanceId").GetGuid().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Tampered_signature_is_rejected()
    {
        var owner = await RegisterOwnerAsync("p6-hook2");
        var (endpointId, secret) = await SetupWebhookAsync(owner, "hooks2");

        var signature = Sign(secret, "original-body");
        var resp = await Fixture.NewClient().SendAsync(Receive(endpointId, "tampered-body", signature));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Same_idempotency_key_returns_the_same_instance()
    {
        var owner = await RegisterOwnerAsync("p6-hook3");
        var (endpointId, secret) = await SetupWebhookAsync(owner, "hooks3");
        const string body = "{\"event\":\"x\"}";
        var signature = Sign(secret, body);

        var first = await Fixture.NewClient().SendAsync(Receive(endpointId, body, signature, "idem-42"));
        var second = await Fixture.NewClient().SendAsync(Receive(endpointId, body, signature, "idem-42"));

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var firstId = (await DataAsync(first)).GetProperty("instanceId").GetGuid();
        (await DataAsync(second)).GetProperty("instanceId").GetGuid().Should().Be(firstId);
    }
}
