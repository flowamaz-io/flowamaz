# Flowamaz.Engine.Runtime

The Flowamaz workflow runtime (MIT): what runs a workflow once it's triggered.

- **Orchestration** (`WorkflowOrchestrator`): trigger (idempotency keys, versions, test runs), step through the graph, resume after a gate, cancel, fail. Every change is an event in the run's event log.
- **Saga compensation** (`SagaEngine`): undo completed steps when a run fails and the workflow declares a compensation block.
- **Node workers:** action steps (`HttpActionWorker` → the connector pipeline), human gates (`HumanGateNodeWorker`: email, Slack and Teams delivery, signed approve/reject links, timeouts), and the worker registry. The AI node worker is supplied by the host.
- **Gates** (`GateService`, `GateHmacHelper`, `GateEmailTemplates`): decide, validate gate forms, and resume the run.
- **Connector pipeline** (`ConnectorActionExecutor`, `ConnectorDispatcher`, `ConnectorOperationHandlerRegistry`): resolve the credential, interpolate inputs, and run the operation in-process or in the sandboxed connector runner.
- **Variables** (`VariableEvaluationService`): resolve `{{ name }}` placeholders and edge conditions against a run's stored variables.
- **Contracts the host implements:** the 7 run entities and their repositories (`IWorkflowInstanceRepository`, …), `IUnitOfWork`, `ITaskQueue`, `IConnectorCredentialResolver`, `IConnectorSandbox`.
- **Runtime hooks** (`IRunAdmission`, `IRunEventSink`, `IStepInterceptor`), with defaults that change nothing. A host plugs in limits, audit, notifications or a debugger here (see the hooks' doc comments).

Built on `Flowamaz.Engine.Core` (parser, validator, conditions) and `Flowamaz.Connectors.Official`. Moved code keeps its original namespaces (`Flowamaz.Application.Workflow.*`, `Flowamaz.Core.*`).

Storage on EF Core and PostgreSQL is in `Flowamaz.Engine.Persistence`; the Redis queue and the background worker are in `Flowamaz.Engine.Hosting`.
