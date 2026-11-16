# Flowamaz.Engine.Hosting

Runs the Flowamaz workflow runtime in a .NET host (MIT).

## A complete host in three calls

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddFlowamazEngineRuntime(builder.Configuration);   // orchestrator, workers, gates
builder.Services.AddFlowamazEnginePersistence(o => o.UseNpgsql(      // Flowamaz.Engine.Persistence
    builder.Configuration.GetConnectionString("Engine"), EngineDbContextOptions.ConfigureNpgsql));
builder.Services.AddFlowamazEngineWorker();                          // background worker + retry wake-ups

await builder.Build().RunAsync();
```

Then trigger a run and decide its approvals from your own code (an API, a CLI, a test):

```csharp
using var scope = host.Services.CreateScope();
var run = await scope.ServiceProvider.GetRequiredService<IWorkflowOrchestrator>()
    .TriggerAsync(workspaceId, workflowId, payload: """{"amount":4200}""", idempotencyKey: null);
// … the worker runs it; at an approval gate the run waits:
await scope.ServiceProvider.GetRequiredService<GateService>()
    .DecideAsync(workspaceId, run.Id, nodeId: "approve", decision: "approved", note: null, decidedBy: userId);
```

To try it without PostgreSQL or Redis, use EF Core's in-memory provider and the default in-memory queue (one process only):

```csharp
builder.Services.AddFlowamazEnginePersistence(o => o.UseInMemoryDatabase("engine")
    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
```

`Flowamaz.Engine.Runtime.Tests/Hosting/InMemoryHostEndToEndTests.cs` is a working example: trigger → HTTP action → approval gate → approve → complete, plus a rejection and a retry.

## What each call registers

- **`AddFlowamazEngineRuntime`**: the parser, orchestrator, saga engine, the HTTP action and approval-gate workers, `GateService` and variable evaluation. It also registers the open defaults for the host hooks: every run admitted (`IRunAdmission`), no run events (`IRunEventSink`), no step debugger (`IStepInterceptor`), and an `IEmailService` that only logs. Register your own implementations to replace any of them (for example an SMTP `IEmailService`, so approvers get the gate email). HTTP actions go through the egress guard, which refuses private and loopback addresses unless `Connectors:AllowPrivateNetworkEgress` is `true`.
- **`AddFlowamazEngineWorker(queue?)`**:
  - **`OrchestratorWorker`**, a `BackgroundService`, polls the queue and leases each run in the database: a 30-second lease, renewed every 10 seconds. `Worker:Concurrency` sets how many runs one worker handles at once (default 4).
  - **`DelayedQueuePromoterService`** wakes runs whose retry backoff has ended, once a second.
  - The queue is **`InMemoryTaskQueue`** by default. Pass a **`RedisTaskQueue`** so several workers, on one machine or several, can share a queue and a database. The Redis queue keeps per-workspace pending and processing lists, so tenants don't block each other. It claims a run atomically (`RPOPLPUSH`), so two workers never take the same run, and keeps delayed retries in a sorted set. It needs an `IConnectionMultiplexer` (StackExchange.Redis).

## Not included yet

- **Connector steps** (`connector: http-rest`, `slack`, …). They need the host to resolve credentials and run handlers (`IConnectorCredentialResolver`, `IConnectorSandbox`). Plain HTTP actions (`config: { url, method }`) work.
- **AI steps.** They need your own model key, with metering; that comes in the next release.

Moved code keeps its original namespaces (`Flowamaz.Infrastructure.Queue`, `Flowamaz.Infrastructure.Workers`).
