using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Tests.Integration.Common;
using YamlDotNet.RepresentationModel;

namespace Flowamaz.Tests.Integration.Phase3;

/// <summary>
/// POST /workflows/create — the unified creation endpoint used by every creation panel.
/// Test host runs with UseStubCompletion=true, so AI methods return non-YAML stub text.
/// </summary>
[Collection("api")]
public class UnifiedCreateEndpointTests : ApiTestBase
{
    public UnifiedCreateEndpointTests(IntegrationApiFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Nl_WithInvalidGeneratedYaml_Returns422_AndPersistsNothing()
    {
        var owner = await RegisterOwnerAsync("uc-invalid");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "uc-invalid");
        const string name = "Stub Generated Workflow";

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/create", new
        {
            name,
            method = "nl",
            nlRequest = new
            {
                workflowName = name,
                purpose = "Approve purchase requests",
                triggerDescription = "Employee submits a request",
                stepsDescription = "Manager approves, finance pays",
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("WORKFLOW_GENERATION_INVALID");
        body.Should().Contain("try again");

        var list = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadAsStringAsync()).Should().NotContain(name);
    }

    [Fact]
    public async Task Canvas_NameWithYamlMetacharacters_ProducesParseableYaml_WithExactName()
    {
        var owner = await RegisterOwnerAsync("uc-canvas");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "uc-canvas");
        const string name = "Ops \"urgent\": refunds #1\nkind: Evil";

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/create",
            new { name, method = "canvas" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var id = (await DataAsync(response)).GetProperty("workflowId").GetGuid();

        var get = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var yaml = (await DataAsync(get)).GetProperty("yamlContent").GetString()!;

        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        ((YamlScalarNode)root["kind"]).Value.Should().Be("Workflow");
        var metadata = (YamlMappingNode)root["metadata"];
        ((YamlScalarNode)metadata["name"]).Value.Should().Be(name);
    }
}
