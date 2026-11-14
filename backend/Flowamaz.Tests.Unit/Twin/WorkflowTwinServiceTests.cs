using FluentAssertions;
using Flowamaz.Application.Workflow.Twin;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Twin;

public class WorkflowTwinServiceTests
{
    private readonly Mock<IWorkflowDefinitionRepository> _definitions = new();
    private readonly Mock<IWorkflowVersionRepository> _versions = new();
    private readonly Mock<IWorkflowTwinRepository> _twins = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private static readonly Guid Ws = Guid.NewGuid();
    private static readonly Guid Wf = Guid.NewGuid();

    private WorkflowTwinService Build() => new(_definitions.Object, _versions.Object, _twins.Object, _uow.Object, new SfgParser(),
        NullLogger<WorkflowTwinService>.Instance);

    private void Definition(string yaml) =>
        _definitions.Setup(d => d.GetByIdForWorkspaceAsync(Wf, Ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition { Id = Wf, WorkspaceId = Ws, YamlContent = yaml });

    [Fact]
    public async Task Unknown_workflow_throws_not_found()
    {
        await Build().Invoking(s => s.RunAsync(Ws, Wf, "manual")).Should().ThrowAsync<WorkflowNotFoundException>();
    }

    [Fact]
    public async Task Unreadable_yaml_is_stored_as_a_failed_run_with_an_actionable_message()
    {
        Definition("nodes: []");
        WorkflowTwinRun? saved = null;
        _twins.Setup(t => t.AddAsync(It.IsAny<WorkflowTwinRun>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowTwinRun, CancellationToken>((r, _) => saved = r);

        var run = await Build().RunAsync(Ws, Wf, "manual");

        run.Status.Should().Be("failed");
        run.ErrorMessage.Should().Contain("couldn't read this workflow").And.Contain("run the Twin again");
        saved!.VersionLabel.Should().Be("draft");
        _twins.Verify(t => t.PruneAsync(Ws, Wf, WorkflowTwinService.RunsKeptPerWorkflow, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Simulates_the_production_version_when_there_is_one()
    {
        Definition("nodes: []"); // the draft is broken…
        _versions.Setup(v => v.GetProductionAsync(Wf, Ws, It.IsAny<CancellationToken>())).ReturnsAsync(new WorkflowVersion
        {
            Id = Guid.NewGuid(), WorkflowDefinitionId = Wf, IsProduction = true,
            YamlContent = "nodes:\n  - { id: s, type: trigger }\n  - { id: e, type: end }\nedges:\n  - { from: s, to: e }\n",
        });
        _twins.Setup(t => t.GetRecentPayloadsAsync(Ws, Wf, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(["not json", "{\"a\":1}"]);
        _twins.Setup(t => t.GetRecordedOutputsAsync(Ws, Wf, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var run = await Build().RunAsync(Ws, Wf, "scheduled");

        run.Status.Should().Be("passed"); // …but production is what runs, and it is fine
        run.VersionLabel.Should().Be("production");
        run.Trigger.Should().Be("scheduled");
    }

    [Fact]
    public async Task One_failing_workflow_does_not_stop_the_scheduled_pass()
    {
        var other = Guid.NewGuid();
        _twins.Setup(t => t.GetDueAsync(It.IsAny<DateTime>(), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TwinDueWorkflow(Ws, Guid.NewGuid(), Guid.NewGuid()), new TwinDueWorkflow(Ws, Wf, Guid.NewGuid())]);
        Definition("nodes:\n  - { id: s, type: trigger }\n  - { id: e, type: end }\nedges:\n  - { from: s, to: e }\n");
        _twins.Setup(t => t.GetRecentPayloadsAsync(Ws, Wf, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _twins.Setup(t => t.GetRecordedOutputsAsync(Ws, Wf, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _ = other;

        (await Build().RunDueAsync(5)).Should().Be(1);
    }

    [Fact]
    public void A_stored_result_that_no_longer_reads_shows_without_findings()
    {
        var dto = WorkflowTwinService.ToDto(new WorkflowTwinRun { FindingsJson = "{oops", Status = "breaks" });
        dto.Findings.Should().BeEmpty();
        dto.Status.Should().Be("breaks");
    }
}
