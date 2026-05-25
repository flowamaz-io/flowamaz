using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Flowamaz.Tests.Integration.Common;

/// <summary>Shared helpers for API integration tests: registration, auth headers, JSON parsing.</summary>
[Collection("api")]
public abstract class ApiTestBase
{
    protected IntegrationApiFixture Fixture { get; }

    protected ApiTestBase(IntegrationApiFixture fixture) => Fixture = fixture;

    protected sealed record Owner(HttpClient Client, string AccessToken, Guid OrgId, string OrgSlug, string Email);

    /// <summary>Registers a new org + owner and returns a client pre-authenticated as that owner.</summary>
    protected async Task<Owner> RegisterOwnerAsync(string slug, string? email = null)
    {
        await Fixture.ResetRedisAsync();
        email ??= $"owner@{slug}.test";
        var client = Fixture.NewClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            orgName = $"Org {slug}",
            orgSlug = slug,
            billingEmail = $"billing@{slug}.test",
            email,
            name = "Owner",
            password = "Sup3rSecret!",
            planSlug = "community",
        });
        response.EnsureSuccessStatusCode();

        var data = await DataAsync(response);
        var token = data.GetProperty("accessToken").GetString()!;
        var orgId = data.GetProperty("user").GetProperty("orgId").GetGuid();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new Owner(client, token, orgId, slug, email);
    }

    /// <summary>Creates a workspace as the given client and returns its id.</summary>
    protected static async Task<Guid> CreateWorkspaceAsync(HttpClient client, string name, string slug)
    {
        var response = await client.PostAsJsonAsync("/api/v1/workspaces", new { name, slug });
        response.EnsureSuccessStatusCode();
        return (await DataAsync(response)).GetProperty("id").GetGuid();
    }

    protected static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return root.GetProperty("data");
    }

    protected static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
    }
}
