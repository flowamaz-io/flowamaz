using Flowamaz.Application.Connectors.Security;
using Flowamaz.Application.Connectors.Services;
using Flowamaz.Application.Workflow.Orchestrator;
using Flowamaz.Application.Workflow.Saga;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Application.Workflow.Workers;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Runtime;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Workflow;
using Flowamaz.Infrastructure.Ai;
using Flowamaz.Infrastructure.Queue;
using Flowamaz.Infrastructure.Services;
using Flowamaz.Infrastructure.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the Flowamaz workflow runtime in a .NET host. A complete host is
/// <c>AddFlowamazEngineRuntime</c> + <c>AddFlowamazEnginePersistence</c> (Flowamaz.Engine.Persistence) +
/// <c>AddFlowamazEngineWorker</c>; README.md has an example.
/// </summary>
public static class EngineHostingServiceCollectionExtensions
{
    /// <summary>
    /// The runtime: parser, orchestrator, saga engine, the HTTP action, AI and approval-gate workers,
    /// <see cref="GateService"/> (approve / reject) and variable evaluation, with the open defaults for the
    /// host hooks (every run admitted, no run events, no step debugger) and an <see cref="IEmailService"/>
    /// that only logs. Register your own implementations before or after this call to replace them.
    /// HTTP actions go through the egress guard: private and loopback addresses are refused unless
    /// <c>Connectors:AllowPrivateNetworkEgress</c> is true.
    /// </summary>
    public static IServiceCollection AddFlowamazEngineRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(configuration);
        services.TryAddSingleton<SfgParser>();

        services.TryAddScoped<IRunAdmission, AllowAllRunAdmission>();
        services.TryAddScoped<IRunEventSink, NullRunEventSink>();
        services.TryAddScoped<IStepInterceptor, NoPauseStepInterceptor>();
        services.TryAddSingleton<IEmailService, LoggingEmailService>();

        services.TryAddScoped<IVariableEvaluationService, VariableEvaluationService>();
        services.TryAddScoped<IWorkflowOrchestrator, WorkflowOrchestrator>();
        services.TryAddScoped<ISagaEngine, SagaEngine>();
        services.TryAddScoped<GateService>();

        var allowPrivate = bool.TryParse(configuration[EgressGuard.ConfigKey], out var allow) && allow;
        services.AddHttpClient(HttpActionWorker.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => EgressGuard.CreateHandler(allowPrivate));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<INodeWorker, HttpActionWorker>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<INodeWorker, HumanGateNodeWorker>());

        // AI steps: the host's own key (Ai:Anthropic:ApiKey) by default; register another IAiModelProvider
        // to use a different provider. Metering (IAiUsageMeter) is required: AddFlowamazEnginePersistence
        // registers one that writes engine_ai_usage.
        services.AddHttpClient(AnthropicModelProvider.HttpClientName);
        services.TryAddScoped<IAiModelProvider, AnthropicModelProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<INodeWorker, AiStepWorker>());
        services.TryAddScoped<INodeWorkerRegistry, NodeWorkerRegistry>();

        // Connector steps (connector: http-rest, slack, …) also need the host to resolve credentials and
        // run handlers (IConnectorCredentialResolver, IConnectorSandbox); a gate's Slack/Teams delivery
        // uses the registry. Without registered handlers it is empty and gates are delivered by email.
        services.TryAddScoped<ConnectorOperationHandlerRegistry>();
        services.TryAddScoped<IConnectorOperationHandlerRegistry>(sp => sp.GetRequiredService<ConnectorOperationHandlerRegistry>());
        return services;
    }

    /// <summary>
    /// The background worker that runs workflows (<see cref="OrchestratorWorker"/>) and the service that
    /// wakes runs after a retry backoff, on <paramref name="queue"/>: a <see cref="RedisTaskQueue"/> for
    /// several processes, or <see cref="InMemoryTaskQueue"/> (the default) for one.
    /// </summary>
    public static IServiceCollection AddFlowamazEngineWorker(this IServiceCollection services, ITaskQueue? queue = null)
    {
        if (queue is not null) services.AddSingleton(queue);
        else services.TryAddSingleton<ITaskQueue, InMemoryTaskQueue>();
        services.AddHostedService<OrchestratorWorker>();
        services.AddHostedService<DelayedQueuePromoterService>();
        return services;
    }
}
