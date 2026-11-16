using System.Net;
using FluentAssertions;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Runtime;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Infrastructure.Persistence.Ai;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Flowamaz.Engine.Runtime.Tests.Hosting;

/// <summary>
/// The open runtime on its own, end to end (open-source step 4d): a real .NET host with the background
/// worker, EF Core's in-memory store and the in-memory queue; no PostgreSQL, Redis or private code. A
/// local HTTP stub stands in for the outside system.
/// </summary>
public sealed class InMemoryHostEndToEndTests : IAsyncLifetime
{
    private readonly Guid _workspaceId = Guid.NewGuid();
    private IHost _host = null!;
    private StubServer _stub = null!;

    private sealed class StubServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly int _failFirst;
        private int _calls;

        public StubServer(int failFirst)
        {
            _failFirst = failFirst;
            var port = Random.Shared.Next(42000, 52000);
            Url = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        public string Url { get; }
        public int Calls => Volatile.Read(ref _calls);

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); } catch { return; }
                var call = Interlocked.Increment(ref _calls);
                ctx.Response.StatusCode = call <= _failFirst ? 500 : 200;
                ctx.Response.ContentType = "application/json";
                await using (var writer = new StreamWriter(ctx.Response.OutputStream))
                    await writer.WriteAsync(call <= _failFirst ? """{"error":"busy"}""" : """{"approved_limit":5000}""");
                ctx.Response.Close();
            }
        }

        public void Dispose() { _listener.Stop(); _listener.Close(); }
    }

    private async Task StartAsync(int failFirst = 0, Action<IServiceCollection>? services = null)
    {
        _stub = new StubServer(failFirst);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Connectors:AllowPrivateNetworkEgress"] = "true", // the stub is on loopback
            ["Worker:Concurrency"] = "2",
        }).Build();
        var databaseName = $"engine-{Guid.NewGuid():N}";

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(collection =>
            {
                services?.Invoke(collection); // registered first, so the runtime's defaults don't replace them
                collection.AddFlowamazEngineRuntime(configuration);
                collection.AddFlowamazEnginePersistence(o => o
                    .UseInMemoryDatabase(databaseName)
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
                collection.AddFlowamazEngineWorker();
            })
            .Build();
        await _host.StartAsync();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_host is not null) { await _host.StopAsync(); _host.Dispose(); }
        _stub?.Dispose();
    }

    private string Yaml(string retry = "") => $$"""
        workflow: { id: loan, version: v1, name: Loan approval }
        nodes:
          - { id: start, type: Trigger }
          - id: score
            type: Action
            label: Credit check
            config: { url: "{{_stub.Url}}score", method: GET }
        {{retry}}
          - id: approve
            type: HumanGate
            label: Manager approval
            config: { assignee: manager@example.com, gate_label: Approve the loan }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: score }
          - { id: e2, from: score, to: approve }
          - { id: e3, from: approve, to: done }
        """;

    private async Task<Guid> PublishAsync(string yaml)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EngineDbContext>();
        var definition = new WorkflowDefinition
        {
            WorkspaceId = _workspaceId, Name = "Loan approval", Slug = "loan", YamlContent = yaml,
            Status = WorkflowStatus.Published, CurrentVersion = "v1",
        };
        db.WorkflowDefinitions.Add(definition);
        db.WorkflowVersions.Add(new WorkflowVersion
        {
            WorkspaceId = _workspaceId, WorkflowDefinitionId = definition.Id, CommitSha = "v1", BranchName = "main",
            YamlContent = yaml, Message = "first", IsProduction = true,
        });
        await db.SaveChangesAsync();
        return definition.Id;
    }

    private async Task<Guid> TriggerAsync(Guid workflowId, string payload = """{"applicant":"Aisha","amount":4200}""")
    {
        using var scope = _host.Services.CreateScope();
        var run = await scope.ServiceProvider.GetRequiredService<IWorkflowOrchestrator>()
            .TriggerAsync(_workspaceId, workflowId, payload, idempotencyKey: null);
        return run.Id;
    }

    private async Task<WorkflowInstance> WaitForAsync(Guid runId, Func<WorkflowInstance, bool> done, TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (true)
        {
            using var scope = _host.Services.CreateScope();
            var run = await scope.ServiceProvider.GetRequiredService<EngineDbContext>()
                .WorkflowInstances.AsNoTracking().FirstAsync(i => i.Id == runId);
            if (done(run)) return run;
            if (DateTime.UtcNow > until)
                throw new TimeoutException($"Run {runId} is still {run.Status} ({run.ErrorMessage}).");
            await Task.Delay(100);
        }
    }

    private async Task DecideAsync(Guid runId, string decision)
    {
        using var scope = _host.Services.CreateScope();
        var gate = await scope.ServiceProvider.GetRequiredService<GateService>()
            .DecideAsync(_workspaceId, runId, "approve", decision, "checked the documents", Guid.NewGuid());
        gate.Should().NotBeNull();
    }

    private async Task<List<string>> EventTypesAsync(Guid runId)
    {
        using var scope = _host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EngineDbContext>().WorkflowEvents.AsNoTracking()
            .Where(e => e.InstanceId == runId).OrderBy(e => e.SequenceNumber).Select(e => e.EventType).ToListAsync();
    }

    [Fact]
    public async Task Trigger_action_gate_approve_complete()
    {
        await StartAsync();
        var runId = await TriggerAsync(await PublishAsync(Yaml()));

        // The worker runs the action, then parks the run at the gate.
        var waiting = await WaitForAsync(runId, r => r.Status == InstanceStatus.Waiting);
        waiting.Status.Should().Be(InstanceStatus.Waiting);
        _stub.Calls.Should().Be(1);
        using (var scope = _host.Services.CreateScope())
        {
            var pending = await scope.ServiceProvider.GetRequiredService<GateService>().ListPendingAsync(_workspaceId);
            pending.Should().ContainSingle().Which.GateLabel.Should().Be("Approve the loan");
        }

        await DecideAsync(runId, "approve");

        var done = await WaitForAsync(runId, r => r.Status is InstanceStatus.Completed or InstanceStatus.Failed);
        done.Status.Should().Be(InstanceStatus.Completed, done.ErrorMessage);
        (await EventTypesAsync(runId)).Should().ContainInOrder(
            "InstanceStarted", "NodeCompleted", "GateDecided", "InstanceCompleted");
    }

    [Fact]
    public async Task A_rejected_gate_ends_the_run()
    {
        await StartAsync();
        var runId = await TriggerAsync(await PublishAsync(Yaml()));
        await WaitForAsync(runId, r => r.Status == InstanceStatus.Waiting);

        await DecideAsync(runId, "reject");

        var done = await WaitForAsync(runId, r => r.Status is InstanceStatus.Completed or InstanceStatus.Failed);
        done.Status.Should().Be(InstanceStatus.Failed);
    }

    [Fact]
    public async Task A_failing_action_is_retried_after_its_backoff_then_the_run_carries_on()
    {
        await StartAsync(failFirst: 1);
        var retry = "    retry: { max_attempts: 2, backoff_seconds: 1, backoff_multiplier: 1 }";
        var runId = await TriggerAsync(await PublishAsync(Yaml(retry)));

        await WaitForAsync(runId, r => r.Status == InstanceStatus.Waiting);

        _stub.Calls.Should().Be(2, "one 500, then the retry after the 1s backoff succeeded");
    }

    /// <summary>A model that answers in JSON and remembers the prompt it was sent (no key, no cost).</summary>
    private sealed class FakeModel : IAiModelProvider
    {
        public string Name => "fake";
        public string? DefaultModel => "fake-model";
        public List<string> Prompts { get; } = [];

        public Task<AiCompletion> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Prompts.Add(request.Prompt);
            return Task.FromResult(new AiCompletion("""{"risk":"low","reason":"small amount"}""", request.Model, 42, 7, 0.0001m));
        }
    }

    [Fact]
    public async Task An_ai_step_runs_on_the_hosts_model_and_is_metered()
    {
        var model = new FakeModel();
        await StartAsync(services: s => s.AddScoped<IAiModelProvider>(_ => model));
        const string yaml = """
            workflow: { id: assess, version: v1, name: Risk check }
            nodes:
              - { id: start, type: Trigger }
              - id: assess
                type: Ai
                config: { prompt: "Rate the risk of a loan of {{ amount }} to {{ applicant }}", max_tokens: 200 }
              - { id: done, type: End }
            edges:
              - { id: e1, from: start, to: assess }
              - { id: e2, from: assess, to: done }
            """;
        var runId = await TriggerAsync(await PublishAsync(yaml));

        var done = await WaitForAsync(runId, r => r.Status is InstanceStatus.Completed or InstanceStatus.Failed);

        done.Status.Should().Be(InstanceStatus.Completed, done.ErrorMessage);
        model.Prompts.Should().ContainSingle().Which.Should().Contain("4200").And.Contain("Aisha");
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EngineDbContext>();
        var usage = await db.Set<EngineAiUsage>().AsNoTracking().SingleAsync();
        (usage.WorkspaceId, usage.InstanceId, usage.NodeId, usage.Provider, usage.ModelId, usage.FunctionId,
            usage.TokensInput, usage.TokensOutput, usage.CostUsd, usage.Succeeded)
            .Should().Be((_workspaceId, (Guid?)runId, "assess", "fake", "fake-model", AiFunctions.NodeExec, 42, 7, 0.0001m, true));
        var output = await db.WorkflowVariables.AsNoTracking().SingleAsync(v => v.InstanceId == runId && v.Name == "assess.output");
        output.Value.Should().Contain("\"risk\":\"low\"");
    }
}
