using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Workflow;

/// <summary>
/// End-to-end workflow + instance API (prompt 02-04): create → validate YAML → publish → trigger →
/// read; invalid YAML → 422; duplicate idempotency key → same instance; cancel → Cancelled;
/// sensitive variables masked.
/// </summary>
[Collection("api")]
public class WorkflowApiTests : ApiTestBase
{
    public WorkflowApiTests(IntegrationApiFixture fixture) : base(fixture) { }

    private const string ValidYaml = """
        workflow:
          id: test-flow
          version: v1
          name: Test Flow
        nodes:
          - { id: start, type: Trigger }
          - id: call
            type: Action
            config: { method: GET, url: "https://example.test/ping" }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: call }
          - { id: e2, from: call, to: done }
        """;

    [Fact]
    public async Task Full_lifecycle_create_publish_trigger_read()
    {
        var owner = await RegisterOwnerAsync("wf-life");
        var ws = await CreateWorkspaceAsync(owner.Client, "Eng", "eng");

        var workflowId = await CreateWorkflowAsync(owner.Client, ws, "Order", "order", ValidYaml);

        var publish = await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(publish)).GetProperty("isProduction").GetBoolean().Should().BeTrue();

        var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances",
            new { workflowDefinitionId = workflowId, payload = """{"x":1}""" });
        trigger.StatusCode.Should().Be(HttpStatusCode.OK);
        var instanceId = (await DataAsync(trigger)).GetProperty("instanceId").GetGuid();
        instanceId.Should().NotBeEmpty();

        var detail = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/instances/{instanceId}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(detail)).GetProperty("id").GetGuid().Should().Be(instanceId);
    }

    [Fact]
    public async Task Create_with_invalid_yaml_returns_422()
    {
        var owner = await RegisterOwnerAsync("wf-bad");
        var ws = await CreateWorkspaceAsync(owner.Client, "Eng", "eng");

        const string invalid = """
            nodes:
              - { id: a, type: Action }
              - { id: done, type: End }
            edges:
              - { id: e1, from: a, to: done }
            """;

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Bad", slug = "bad", yamlContent = invalid, createdByMethod = "NaturalLanguage" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await MessageAsync(response)).Should().Contain("Trigger");
    }

    [Fact]
    public async Task Duplicate_idempotency_key_returns_same_instance()
    {
        var owner = await RegisterOwnerAsync("wf-idem");
        var ws = await CreateWorkspaceAsync(owner.Client, "Eng", "eng");
        var workflowId = await CreateWorkflowAsync(owner.Client, ws, "Idem", "idem", ValidYaml);
        await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null);

        var body = new { workflowDefinitionId = workflowId, idempotencyKey = "key-123" };
        var first = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances", body);
        var second = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances", body);

        var firstId = (await DataAsync(first)).GetProperty("instanceId").GetGuid();
        var secondId = (await DataAsync(second)).GetProperty("instanceId").GetGuid();
        secondId.Should().Be(firstId);
    }

    [Fact]
    public async Task Cancel_sets_status_cancelled()
    {
        var owner = await RegisterOwnerAsync("wf-cancel");
        var ws = await CreateWorkspaceAsync(owner.Client, "Eng", "eng");
        var workflowId = await CreateWorkflowAsync(owner.Client, ws, "Cancel", "cancel", ValidYaml);
        await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null);

        var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances",
            new { workflowDefinitionId = workflowId });
        var instanceId = (await DataAsync(trigger)).GetProperty("instanceId").GetGuid();

        var cancel = await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/instances/{instanceId}/cancel", null);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(cancel)).GetProperty("status").GetString().Should().Be("Cancelled");
    }

    [Fact]
    public async Task Sensitive_variables_are_masked()
    {
        var owner = await RegisterOwnerAsync("wf-mask");
        var ws = await CreateWorkspaceAsync(owner.Client, "Eng", "eng");
        var workflowId = await CreateWorkflowAsync(owner.Client, ws, "Mask", "mask", ValidYaml);
        await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null);
        var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances",
            new { workflowDefinitionId = workflowId });
        var instanceId = (await DataAsync(trigger)).GetProperty("instanceId").GetGuid();

        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            db.WorkflowVariables.Add(new WorkflowVariable
            {
                WorkspaceId = ws, InstanceId = instanceId, Name = "apiToken", Value = "\"super-secret\"", IsSensitive = true,
            });
            db.WorkflowVariables.Add(new WorkflowVariable
            {
                WorkspaceId = ws, InstanceId = instanceId, Name = "publicVar", Value = "\"ok\"", IsSensitive = false,
            });
            await db.SaveChangesAsync();
        }

        var variables = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/instances/{instanceId}/variables");
        variables.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await DataAsync(variables);
        var secret = data.EnumerateArray().First(v => v.GetProperty("name").GetString() == "apiToken");
        secret.GetProperty("value").GetString().Should().Be("***");
    }

    private async Task<Guid> CreateWorkflowAsync(HttpClient client, Guid ws, string name, string slug, string yaml)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name, slug, yamlContent = yaml, createdByMethod = "NaturalLanguage" });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await DataAsync(response)).GetProperty("id").GetGuid();
    }
}
