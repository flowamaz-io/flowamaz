using FluentAssertions;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow;

/// <summary>The gates inbox shows a gate's name (gate_label, else the node label), not its node id.</summary>
public class GateLabelTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly Guid _versionId = Guid.NewGuid();

    private GateService NewService(string yaml, params GateDecision[] gates)
    {
        var gateRepo = new Mock<IGateDecisionRepository>();
        gateRepo.Setup(r => r.GetPendingForWorkspaceAsync(_workspaceId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(gates.ToList());
        var instances = new Mock<IWorkflowInstanceRepository>();
        instances.Setup(r => r.GetByIdForWorkspaceAsync(_instanceId, _workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowInstance { Id = _instanceId, WorkspaceId = _workspaceId, WorkflowVersionId = _versionId });
        var versions = new Mock<IWorkflowVersionRepository>();
        versions.Setup(r => r.GetByIdForWorkspaceAsync(_versionId, _workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowVersion { Id = _versionId, WorkspaceId = _workspaceId, YamlContent = yaml });

        return new GateService(gateRepo.Object, Mock.Of<IWorkflowEventRepository>(), Mock.Of<IWorkflowOrchestrator>(),
            Mock.Of<IUnitOfWork>(), NullLogger<GateService>.Instance,
            instances: instances.Object, versions: versions.Object);
    }

    private GateDecision Gate(string nodeId) => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = _workspaceId, InstanceId = _instanceId, NodeId = nodeId, Decision = GateDecisionStatus.Pending,
    };

    private const string Yaml = """
        workflow: { id: w, version: v1, name: W }
        nodes:
          - { id: start, type: Trigger }
          - id: approve
            type: HumanGate
            label: Manager approval
            config: { gate_label: Approve PR45 request }
          - id: second
            type: HumanGate
            label: Finance sign-off
          - { id: bare, type: HumanGate }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: approve }
          - { id: e2, from: approve, to: second }
          - { id: e3, from: second, to: bare }
          - { id: e4, from: bare, to: done }
        """;

    [Fact]
    public async Task Pending_gates_carry_the_gate_label_and_node_label_from_the_runs_version()
    {
        var service = NewService(Yaml, Gate("approve"), Gate("second"), Gate("bare"));

        var gates = await service.ListPendingAsync(_workspaceId);

        gates.Select(g => (g.NodeId, g.GateLabel, g.Label)).Should().Equal(
            ("approve", "Approve PR45 request", "Manager approval"),
            ("second", null, "Finance sign-off"),
            ("bare", null, null)); // the UI humanises the id
    }

    [Fact]
    public async Task A_version_that_does_not_parse_leaves_the_names_empty_instead_of_failing_the_list()
    {
        var service = NewService("not: [valid", Gate("approve"));

        var gates = await service.ListPendingAsync(_workspaceId);

        gates.Should().ContainSingle().Which.Label.Should().BeNull();
    }
}
