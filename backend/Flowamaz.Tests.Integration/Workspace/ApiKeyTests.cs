using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Workspace;

[Collection("api")]
public class ApiKeyTests : ApiTestBase
{
    public ApiKeyTests(IntegrationApiFixture fixture) : base(fixture) { }

    private async Task<Guid> ProductionEnvironmentIdAsync(Guid workspaceId)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var env = await db.WorkspaceEnvironments
            .FirstAsync(e => e.WorkspaceId == workspaceId && e.Name == WorkspaceEnvironmentType.Production);
        return env.Id;
    }

    [Fact]
    public async Task Create_returns_plain_key_once_with_one_time_header()
    {
        var owner = await RegisterOwnerAsync("key-create");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "WS", "key-ws");
        var envId = await ProductionEnvironmentIdAsync(workspaceId);

        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/api-keys",
            new { name = "CI", environmentId = envId, scopes = new[] { "workflows:read" }, expiresAt = (DateTime?)null });

        create.StatusCode.Should().Be(HttpStatusCode.OK);
        create.Headers.GetValues("X-Plain-Key-One-Time").Should().Contain("true");
        var data = await DataAsync(create);
        data.GetProperty("plainKey").GetString().Should().StartWith("fmz_live_");
    }

    [Fact]
    public async Task List_never_exposes_the_plain_key()
    {
        var owner = await RegisterOwnerAsync("key-list");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "WS", "keylist-ws");
        var envId = await ProductionEnvironmentIdAsync(workspaceId);

        await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/api-keys",
            new { name = "CI", environmentId = envId, scopes = new[] { "workflows:read" }, expiresAt = (DateTime?)null });

        var list = await owner.Client.GetAsync($"/api/v1/workspaces/{workspaceId}/api-keys");
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = (await DataAsync(list)).GetProperty("data");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("plainKey").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Revoke_then_validation_fails()
    {
        var owner = await RegisterOwnerAsync("key-revoke");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "WS", "keyrevoke-ws");
        var envId = await ProductionEnvironmentIdAsync(workspaceId);

        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/api-keys",
            new { name = "CI", environmentId = envId, scopes = new[] { "workflows:read" }, expiresAt = (DateTime?)null });
        var created = await DataAsync(create);
        var keyId = created.GetProperty("id").GetGuid();
        var plainKey = created.GetProperty("plainKey").GetString()!;

        using (var scope = Fixture.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IWorkspaceApiKeyService>();
            (await svc.ValidateApiKeyAsync(plainKey)).Should().NotBeNull("the key is valid before revocation");
        }

        var revoke = await owner.Client.DeleteAsync($"/api/v1/workspaces/{workspaceId}/api-keys/{keyId}");
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = Fixture.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IWorkspaceApiKeyService>();
            (await svc.ValidateApiKeyAsync(plainKey)).Should().BeNull("a revoked key must not validate");
        }
    }
}
