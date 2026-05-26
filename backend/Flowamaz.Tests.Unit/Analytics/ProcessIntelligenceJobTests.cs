using FluentAssertions;
using Flowamaz.Application.Analytics;
using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Tests.Unit.Analytics;

/// <summary>
/// Process Intelligence deterministic logic (prompt 02-07): SLA-risk evaluation (no AI), percentile
/// + metric build, and the insight upsert that replaces an unacknowledged insight rather than
/// duplicating it.
/// </summary>
public class ProcessIntelligenceJobTests
{
    [Fact]
    public void EvaluateSlaRisk_fires_when_avg_exceeds_80pct_of_threshold()
    {
        // threshold 100, avg 85 > 80 → risk
        var message = ProcessIntelligenceService.EvaluateSlaRisk("Order", avgDurationMs: 85, slaThresholdMs: 100);
        message.Should().NotBeNull();
        message.Should().Contain("SLA");
    }

    [Fact]
    public void EvaluateSlaRisk_silent_when_under_threshold_or_no_sla()
    {
        ProcessIntelligenceService.EvaluateSlaRisk("Order", avgDurationMs: 50, slaThresholdMs: 100).Should().BeNull();
        ProcessIntelligenceService.EvaluateSlaRisk("Order", avgDurationMs: 9999, slaThresholdMs: null).Should().BeNull();
    }

    [Fact]
    public void BuildMetric_counts_statuses_and_computes_average()
    {
        var samples = new List<InstanceDurationSample>
        {
            new(InstanceStatus.Completed, 100),
            new(InstanceStatus.Completed, 200),
            new(InstanceStatus.Failed, null),
            new(InstanceStatus.Cancelled, null),
        };

        var metric = ProcessIntelligenceService.BuildMetric(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, samples, null);

        metric.RunsTotal.Should().Be(4);
        metric.RunsCompleted.Should().Be(2);
        metric.RunsFailed.Should().Be(1);
        metric.RunsCancelled.Should().Be(1);
        metric.AvgDurationMs.Should().Be(150);
    }

    [Fact]
    public void Percentile_picks_the_expected_rank()
    {
        var sorted = new List<long> { 10, 20, 30, 40, 50 };
        ProcessIntelligenceService.Percentile(sorted, 95).Should().Be(50);
        ProcessIntelligenceService.Percentile(sorted, 50).Should().Be(30);
        ProcessIntelligenceService.Percentile([], 95).Should().Be(0);
    }

    [Fact]
    public async Task ReplaceUnacknowledged_replaces_not_duplicates()
    {
        await using var db = new FlowAmazDbContext(
            new DbContextOptionsBuilder<FlowAmazDbContext>().UseInMemoryDatabase($"insights-{Guid.NewGuid():N}").Options);
        IWorkflowInsightRepository repo = new WorkflowInsightRepository(db);
        var ws = Guid.NewGuid();
        var def = Guid.NewGuid();

        await repo.AddAsync(NewSlaRisk(ws, def, "first"));
        await db.SaveChangesAsync();

        await repo.ReplaceUnacknowledgedAsync(NewSlaRisk(ws, def, "second"));
        await db.SaveChangesAsync();

        var live = await repo.GetUnacknowledgedForWorkspaceAsync(ws);
        live.Should().ContainSingle();
        live[0].Message.Should().Be("second");
    }

    private static WorkflowInsight NewSlaRisk(Guid ws, Guid def, string message) => new()
    {
        WorkspaceId = ws,
        WorkflowDefinitionId = def,
        InsightType = InsightType.SlaRisk,
        Severity = InsightSeverity.Warning,
        Message = message,
    };
}
