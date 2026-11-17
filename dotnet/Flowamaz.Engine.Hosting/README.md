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

Then create and publish a workflow, trigger a run and decide its approvals from your own code (an API, a CLI, a test):

```csharp
using var scope = host.Services.CreateScope();
var workflows = scope.ServiceProvider.GetRequiredService<WorkflowService>();
var workflow = await workflows.CreateAsync(workspaceId, userId, new CreateWorkflowDefinitionRequest(
    "Loan approval", "loan", yaml, NlDescription: null, WorkflowCreatedByMethod.Canvas));
await workflows.PublishAsync(workspaceId, workflow.Id, userId);   // validates the YAML first
var workflowId = workflow.Id;
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

`Flowamaz.Engine.Runtime.Tests/Hosting/InMemoryHostEndToEndTests.cs` is a working example: create and publish → trigger → HTTP action → approval gate → approve → complete, a rejection, a retry, a metered AI step, and reading the run back (list, detail, timeline).

## What each call registers

- **`AddFlowamazEngineRuntime`**: the parser, orchestrator, saga engine, the HTTP action and approval-gate workers, `GateService`, `WorkflowService` (create, save drafts, publish, archive, versions), `InstanceService` (run list, detail, events, timeline, retry) and variable evaluation. It also registers the open defaults for the host hooks: every run admitted (`IRunAdmission`), no run events (`IRunEventSink`), no step debugger (`IStepInterceptor`), every workflow admitted (`IWorkflowAdmission`), no workflow events (`IWorkflowEventSink`), versions kept in the database only (`IWorkflowVersionStore`: each save gets an opaque reference, each release the tag `v{n}`), and an `IEmailService` that only logs. Register your own implementations to replace any of them (for example an SMTP `IEmailService`, so approvers get the gate email). HTTP actions go through the egress guard, which refuses private and loopback addresses unless `Connectors:AllowPrivateNetworkEgress` is `true`.
- **AI steps** (`type: ai`, in `AddFlowamazEngineRuntime`). They run on your own model key: Anthropic by default (`AnthropicModelProvider`), or any `IAiModelProvider` you register. Settings:

  ```json
  "Ai": {
    "Anthropic": { "ApiKey": "<from a secret store, e.g. env Ai__Anthropic__ApiKey>", "Model": "<default model id>" },
    "Pricing": { "<model id>": { "InputUsdPerMillionTokens": 1.0, "OutputUsdPerMillionTokens": 5.0 } }
  }
  ```

  - `Model` is used by steps that don't name one; no model id is built in. `BaseUrl` and `TimeoutSeconds` are optional.
  - Without `Pricing` the cost is recorded as 0, but the token counts are still exact.
  - **Every AI call is metered (required).** `AddFlowamazEnginePersistence` registers a meter that writes `engine_ai_usage`: workspace, run, step, provider, model, tokens in and out, cost, and whether the call succeeded. Failed calls are metered too. There is no do-nothing meter: if usage can't be recorded, the step fails rather than run unmetered.
  - Sensitive variables (stored encrypted) never reach the model: their placeholders become `[SENSITIVE_REDACTED]` before the prompt is filled.
  - Not in the open step: `output_schema` validation, fallback models, cost caps, workspace budgets and per-org model policy (the Flowamaz product has these).
- **`AddFlowamazEngineWorker(queue?)`**:
  - **`OrchestratorWorker`**, a `BackgroundService`, polls the queue and leases each run in the database: a 30-second lease, renewed every 10 seconds. `Worker:Concurrency` sets how many runs one worker handles at once (default 4).
  - **`DelayedQueuePromoterService`** wakes runs whose retry backoff has ended, once a second.
  - The queue is **`InMemoryTaskQueue`** by default. Pass a **`RedisTaskQueue`** so several workers, on one machine or several, can share a queue and a database. The Redis queue keeps per-workspace pending and processing lists, so tenants don't block each other. It claims a run atomically (`RPOPLPUSH`), so two workers never take the same run, and keeps delayed retries in a sorted set. It needs an `IConnectionMultiplexer` (StackExchange.Redis).

## Not included yet

- **Connector steps** (`connector: http-rest`, `slack`, …). They need the host to resolve credentials and run handlers (`IConnectorCredentialResolver`, `IConnectorSandbox`). Plain HTTP actions (`config: { url, method }`) work.

Moved code keeps its original namespaces (`Flowamaz.Infrastructure.Queue`, `Flowamaz.Infrastructure.Workers`).
