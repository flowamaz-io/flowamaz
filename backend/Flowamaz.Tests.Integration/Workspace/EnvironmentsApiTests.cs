using System.Net;
using FluentAssertions;
using Flowamaz.Tests.Integration.Common;

namespace Flowamaz.Tests.Integration.Workspace;

[Collection("api")]
public class EnvironmentsApiTests : ApiTestBase
{
    public EnvironmentsApiTests(IntegrationApiFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Get_environments_returns_three_in_dev_staging_production_order()
    {
        var owner = await RegisterOwnerAsync("ws-envs");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "Env WS", "env-ws");

        var get = await owner.Client.GetAsync($"/api/v1/workspaces/{workspaceId}/environments");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var data = await DataAsync(get);
        data.GetArrayLength().Should().Be(3, "every workspace is provisioned with Dev/Staging/Production");

        var names = data.EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();
        names.Should().ContainInOrder("Dev", "Staging", "Production");

        foreach (var env in data.EnumerateArray())
        {
            env.GetProperty("id").GetGuid().Should().NotBeEmpty();
            env.GetProperty("workspaceId").GetGuid().Should().Be(workspaceId);
        }
    }

    [Fact]
    public async Task Get_environments_for_another_orgs_workspace_is_denied()
    {
        var ownerA = await RegisterOwnerAsync("ws-env-a");
        var workspaceId = await CreateWorkspaceAsync(ownerA.Client, "A", "env-a");

        var ownerB = await RegisterOwnerAsync("ws-env-b");
        var get = await ownerB.Client.GetAsync($"/api/v1/workspaces/{workspaceId}/environments");

        // Another org's workspace returns 404 (never 403) so its existence is not revealed.
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
