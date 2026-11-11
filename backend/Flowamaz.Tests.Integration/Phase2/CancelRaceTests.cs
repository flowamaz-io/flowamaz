using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase2;

/// <summary>
/// A cancel that lands while the worker is taking a run's first step (seen from the loan app: withdrawn 0.2 s after
/// submitting). Before the row lock, the step saved "Running" over "Cancelled" and both wrote the same event
/// sequence number, leaving the run stuck in Running. Now the two happen one after the other, whichever goes first.
/// </summary>
[Collection("api")]
public sealed class CancelRaceTests : ApiTestBase
{
    public CancelRaceTests(IntegrationApiFixture fixture) : base(fixture) { }

    private const string Yaml = """
        workflow: { id: race, version: v1, name: Race }
        nodes:
          - { id: start, type: Trigger }
          - { id: check, type: Action }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: check }
          - { id: e2, from: check, to: done }
        """;

    [Fact]
    public async Task A_cancel_racing_the_first_step_always_leaves_the_run_cancelled()
    {
        var owner = await RegisterOwnerAsync("race-cancel");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "ops");
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Race", slug = "race", yamlContent = Yaml, createdByMethod = "NaturalLanguage" });
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();
        await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null);

        for (var round = 0; round < 15; round++)
        {
            var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances", new { workflowDefinitionId = workflowId });
            var instanceId = (await DataAsync(trigger)).GetProperty("instanceId").GetGuid();

            using var stepScope = Fixture.Services.CreateScope();
            using var cancelScope = Fixture.Services.CreateScope();
            var stepper = stepScope.ServiceProvider.GetRequiredService<IWorkflowOrchestrator>();
            var canceller = cancelScope.ServiceProvider.GetRequiredService<IWorkflowOrchestrator>();

            // Start both at the same moment; alternate which one is handed to the thread pool first.
            using var go = new ManualResetEventSlim();
            var step = Task.Run(async () => { go.Wait(); await stepper.StepAsync(instanceId, "race-worker"); });
            var cancel = Task.Run(async () => { go.Wait(); if (round % 2 == 0) await Task.Yield(); await canceller.CancelAsync(instanceId, Guid.Empty); });
            go.Set();
            await FluentActions.Awaiting(() => Task.WhenAll(step, cancel)).Should().NotThrowAsync($"round {round}");

            // A node result arriving after the cancel changes nothing.
            using (var lateScope = Fixture.Services.CreateScope())
                await lateScope.ServiceProvider.GetRequiredService<IWorkflowOrchestrator>()
                    .CompleteNodeAsync(instanceId, "check", "{}", "race-worker");

            using var verify = Fixture.Services.CreateScope();
            var db = verify.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            var instance = await db.WorkflowInstances.AsNoTracking().IgnoreQueryFilters().SingleAsync(i => i.Id == instanceId);
            instance.Status.Should().Be(InstanceStatus.Cancelled, $"round {round}");
            var open = await db.WorkflowNodeStates.AsNoTracking().IgnoreQueryFilters()
                .CountAsync(n => n.InstanceId == instanceId && (n.Status == NodeStatus.Pending || n.Status == NodeStatus.Running));
            open.Should().Be(0, $"round {round}: nothing is left running after a cancel");
            var events = await db.WorkflowEvents.AsNoTracking().IgnoreQueryFilters()
                .Where(e => e.InstanceId == instanceId).OrderBy(e => e.SequenceNumber).Select(e => e.EventType).ToListAsync();
            events.Last().Should().Be("InstanceCancelled", $"round {round}: nothing happens to the run after it's cancelled");
        }
    }
}
