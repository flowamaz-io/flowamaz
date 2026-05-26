using FluentAssertions;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase2;

/// <summary>
/// Saga engine over real Postgres (prompt 02-08). Backward drives a failed instance to a clean
/// Failed/CompensationCompleted outcome; Forward resets the failed node and resumes. (Reverse
/// compensation ordering is asserted in the SagaEngine unit test with a recording worker.)
/// </summary>
[Collection("api")]
public class SagaTests : ApiTestBase
{
    public SagaTests(IntegrationApiFixture fixture) : base(fixture) { }

    private const string Yaml = """
        workflow: { id: saga, version: v1, name: Saga }
        nodes:
          - { id: start, type: Trigger }
          - { id: A, type: Action, compensation: { strategy: backward, compensateNodeId: cA } }
          - { id: B, type: Action, compensation: { strategy: backward, compensateNodeId: cB } }
          - { id: C, type: Action, compensation: { strategy: backward, compensateNodeId: cC } }
          - { id: cA, type: Wait }
          - { id: cB, type: Wait }
          - { id: cC, type: Wait }
          - { id: done, type: End }
        edges:
          - { from: start, to: A }
          - { from: A, to: B }
          - { from: B, to: C }
          - { from: C, to: done }
          - { from: start, to: cA }
          - { from: start, to: cB }
          - { from: start, to: cC }
        """;

    [Fact]
    public async Task Backward_drives_instance_to_failed_with_compensation_events()
    {
        var ws = Guid.NewGuid();
        var instanceId = await SeedAsync(ws, completedActionNodes: ["A", "B", "C"], failedNode: null);

        using (var scope = Fixture.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISagaEngine>()
                .StartAsync(instanceId, "C", SagaStrategyType.Backward);
        }

        using var verify = Fixture.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var instance = await db.WorkflowInstances.IgnoreQueryFilters().FirstAsync(i => i.Id == instanceId);
        instance.Status.Should().Be(InstanceStatus.Failed);
        instance.SagaState.Should().Be(SagaState.None);

        var events = await db.WorkflowEvents.Where(e => e.InstanceId == instanceId).Select(e => e.EventType).ToListAsync();
        events.Should().Contain("CompensationStarted");
        events.Should().Contain("CompensationCompleted");
    }

    [Fact]
    public async Task Forward_resets_failed_node_and_resumes_running()
    {
        var ws = Guid.NewGuid();
        var instanceId = await SeedAsync(ws, completedActionNodes: ["A"], failedNode: "B");

        using (var scope = Fixture.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISagaEngine>()
                .StartAsync(instanceId, "B", SagaStrategyType.Forward);
        }

        using var verify = Fixture.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var instance = await db.WorkflowInstances.FirstAsync(i => i.Id == instanceId);
        instance.Status.Should().Be(InstanceStatus.Running);
        instance.SagaState.Should().Be(SagaState.None);

        var nodeB = await db.WorkflowNodeStates.FirstAsync(n => n.InstanceId == instanceId && n.NodeId == "B");
        nodeB.Status.Should().Be(NodeStatus.Pending);
        nodeB.RetryCount.Should().Be(0);
    }

    private async Task<Guid> SeedAsync(Guid ws, string[] completedActionNodes, string? failedNode)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();

        var versionId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        db.WorkflowVersions.Add(new WorkflowVersion
        {
            Id = versionId, WorkspaceId = ws, WorkflowDefinitionId = Guid.NewGuid(),
            CommitSha = Guid.NewGuid().ToString("N"), BranchName = "main", YamlContent = Yaml, Message = "seed",
        });
        db.WorkflowInstances.Add(new WorkflowInstance
        {
            Id = instanceId, WorkspaceId = ws, WorkflowDefinitionId = Guid.NewGuid(),
            WorkflowVersionId = versionId, Status = InstanceStatus.Running, StartedAt = DateTime.UtcNow,
        });

        var t = DateTime.UtcNow;
        foreach (var (nodeId, index) in completedActionNodes.Select((n, i) => (n, i)))
        {
            db.WorkflowNodeStates.Add(new WorkflowNodeState
            {
                WorkspaceId = ws, InstanceId = instanceId, NodeId = nodeId, NodeType = "Action",
                Status = NodeStatus.Completed, StartedAt = t.AddSeconds(index), CompletedAt = t.AddSeconds(index + 1),
            });
        }

        if (failedNode is not null)
        {
            db.WorkflowNodeStates.Add(new WorkflowNodeState
            {
                WorkspaceId = ws, InstanceId = instanceId, NodeId = failedNode, NodeType = "Action",
                Status = NodeStatus.Failed, RetryCount = 3, ErrorMessage = "boom",
            });
        }

        await db.SaveChangesAsync();
        return instanceId;
    }
}
