using FluentAssertions;
using Flowamaz.Application.Workflow.Scheduling;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Jobs;

public class ScheduleTriggerJobTests
{
    private const string Yaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata: { id: hourly, name: Hourly }
        spec:
          trigger:
            type: schedule
            config: { cron: "CRON" }
          nodes:
            - { id: start, type: trigger }
            - { id: done, type: end }
          edges:
            - { id: e1, from: start, to: done }
        """;

    private readonly Mock<IWorkflowDefinitionRepository> _definitions = new();
    private readonly Mock<IWorkflowVersionRepository> _versions = new();
    private readonly Mock<IWorkflowOrchestrator> _orchestrator = new();
    private readonly WorkflowDefinition _def = new() { Id = Guid.NewGuid(), WorkspaceId = Guid.NewGuid(), Name = "Hourly", Slug = "hourly" };

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private ScheduleTriggerJob Job(DateTime nowUtc, string cron)
    {
        _definitions.Setup(d => d.GetPublishedScheduledAsync(It.IsAny<CancellationToken>())).ReturnsAsync([_def]);
        _versions.Setup(v => v.GetProductionAsync(_def.Id, _def.WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowVersion { WorkflowDefinitionId = _def.Id, WorkspaceId = _def.WorkspaceId, YamlContent = Yaml.Replace("CRON", cron) });
        return new ScheduleTriggerJob(_definitions.Object, _versions.Object, _orchestrator.Object,
            NullLogger<ScheduleTriggerJob>.Instance, new FixedClock(new DateTimeOffset(nowUtc, TimeSpan.Zero)));
    }

    [Fact]
    public async Task FiresTheDueOccurrence_OnceWithAStableIdempotencyKey()
    {
        var job = Job(new DateTime(2026, 10, 23, 9, 0, 30, DateTimeKind.Utc), "0 * * * *"); // hourly at :00

        (await job.RunOnceAsync(default)).Should().Be(1);

        _orchestrator.Verify(o => o.TriggerAsync(_def.WorkspaceId, _def.Id, It.IsAny<string?>(),
            $"schedule:{_def.Id:N}:20261023090000", InstanceTriggerType.Schedule, null, false, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DoesNotFire_WhenNoOccurrenceInTheCatchUpWindow()
    {
        var job = Job(new DateTime(2026, 10, 23, 9, 30, 0, DateTimeKind.Utc), "0 * * * *");

        (await job.RunOnceAsync(default)).Should().Be(0);
        _orchestrator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DuplicateFromAnotherInstance_IsSwallowed()
    {
        var job = Job(new DateTime(2026, 10, 23, 9, 1, 0, DateTimeKind.Utc), "0 * * * *");
        _orchestrator.Setup(o => o.TriggerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<InstanceTriggerType>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("duplicate key"));

        var act = () => job.RunOnceAsync(default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ArchivedWorkspace_IsSkipped_WithoutStoppingTheJob()
    {
        var job = Job(new DateTime(2026, 10, 23, 9, 1, 0, DateTimeKind.Utc), "0 * * * *");
        _orchestrator.Setup(o => o.TriggerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<InstanceTriggerType>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkspaceArchivedException());

        (await job.RunOnceAsync(default)).Should().Be(0);
    }

    [Theory]
    [InlineData("""
        spec:
          trigger: { type: schedule, config: { cron: "0 9 * * *", timezone: UTC } }
          nodes: []
        """, WorkflowTriggerType.Schedule, "0 9 * * *")]
    [InlineData("""
        nodes:
          - { id: start, type: Trigger, config: { trigger_type: schedule, cron: "*/15 * * * *" } }
        """, WorkflowTriggerType.Schedule, "*/15 * * * *")]
    [InlineData("""
        spec:
          trigger: { type: webhook }
        """, WorkflowTriggerType.Webhook, null)]
    [InlineData("not: [valid", WorkflowTriggerType.Manual, null)]
    public void TriggerReader_ReadsSpecAndCanvasForms(string yaml, WorkflowTriggerType type, string? cron)
    {
        var info = WorkflowTriggerReader.Read(yaml);
        info.Type.Should().Be(type);
        info.Cron.Should().Be(cron);
    }
}
