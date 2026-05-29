using System.Net.Http.Json;
using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase5;

/// <summary>
/// ROI analytics end-to-end (prompt 05-08): configure a baseline, seed run metrics, then read the
/// ROI summary and assert deterministic time-saved / cost-avoided figures.
/// </summary>
[Collection("api")]
public class Phase5RoiTests : ApiTestBase
{
    public Phase5RoiTests(IntegrationApiFixture fixture) : base(fixture) { }

    private const string Yaml = """
        workflow: { id: w, version: v1, name: Orders }
        nodes:
          - { id: start, type: Trigger }
          - { id: a, type: Action }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: a }
          - { id: e2, from: a, to: done }
        """;

    [Fact]
    public async Task Configure_roi_then_get_summary_returns_correct_time_saved_and_cost_avoided()
    {
        var owner = await RegisterOwnerAsync("roi-e2e");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "ROI WS", "roi-e2e-ws");

        var createResp = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows",
            new { name = "Orders", slug = "orders", yamlContent = Yaml, nlDescription = (string?)null, createdByMethod = "NaturalLanguage" });
        createResp.EnsureSuccessStatusCode();
        var workflowId = (await DataAsync(createResp)).GetProperty("id").GetGuid();

        // Admin configures the ROI baseline: 30 min manual, $20 manual, $2 automated.
        var cfgResp = await owner.Client.PutAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/roi-config",
            new { manualProcessTimeMinutes = 30, manualProcessCostPerRunUsd = 20m, automationCostPerRunUsd = 2m, monthlyCurrency = "USD" });
        cfgResp.EnsureSuccessStatusCode();

        // Seed a metric row: 10 runs, 8 completed this hour.
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            db.WorkflowMetrics.Add(new WorkflowMetric
            {
                WorkspaceId = workspaceId,
                WorkflowDefinitionId = workflowId,
                PeriodHour = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, 0, 0, DateTimeKind.Utc),
                RunsTotal = 10,
                RunsCompleted = 8,
            });
            await db.SaveChangesAsync();
        }

        var from = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).ToString("o");
        var to = DateTime.UtcNow.AddHours(1).ToString("o");
        var roiResp = await owner.Client.GetAsync($"/api/v1/workspaces/{workspaceId}/analytics/roi?from={from}&to={to}");
        roiResp.EnsureSuccessStatusCode();
        var data = await DataAsync(roiResp);

        // time saved = 30 * 8 = 240 min; cost avoided = (20-2) * 8 = 144; ROI% = 144 / (2*8=16) * 100 = 900.
        data.GetProperty("totalTimeSavedMinutes").GetInt64().Should().Be(240);
        data.GetProperty("totalCostAvoided").GetDecimal().Should().Be(144m);
        data.GetProperty("roiPercentage").GetDecimal().Should().Be(900m);
    }
}
