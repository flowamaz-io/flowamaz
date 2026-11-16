using Flowamaz.Application.Connectors;
using Flowamaz.Application.Connectors.Handlers;
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
    /// <see cref="GateService"/> (approve / reject), <see cref="WorkflowService"/> (create, save, publish, archive),
    /// <see cref="InstanceService"/> (run list, detail, timeline, retry) and variable evaluation, with the open
    /// defaults for the host hooks (every run and workflow admitted, versions kept in the database only, no run or
    /// workflow events, no step debugger) and an <see cref="IEmailService"/>
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
        services.TryAddSingleton<IWorkflowVersionStore>(DatabaseWorkflowVersionStore.Instance);
        services.TryAddSingleton<IWorkflowAdmission>(AllowAllWorkflowAdmission.Instance);
        services.TryAddSingleton<IWorkflowEventSink>(NullWorkflowEventSink.Instance);

        services.TryAddScoped<IVariableEvaluationService, VariableEvaluationService>();
        services.TryAddScoped<IWorkflowOrchestrator, WorkflowOrchestrator>();
        services.TryAddScoped<ISagaEngine, SagaEngine>();
        services.TryAddScoped<GateService>();
        services.TryAddScoped<WorkflowService>();
        services.TryAddScoped<InstanceService>();

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
    /// Connector steps (<c>connector_id</c> + <c>operation</c>) with the official connectors running in this process:
    /// HTTP/REST, Slack, Teams, email, GitHub, PostgreSQL, MySQL, webhook emit, schedule, Microsoft 365, file system
    /// and shell. Every outbound HTTP client goes through the egress guard. The file-system and shell connectors stay
    /// off until configured (<c>Connectors:FileSystem:RootPath</c>, <c>Connectors:ScriptShell:Enabled</c>), and the
    /// email connector sends through the host's <see cref="IEmailService"/>. The host must register an
    /// <see cref="IConnectorCredentialResolver"/>: credentials are always the host's to store.
    /// </summary>
    public static IServiceCollection AddFlowamazOfficialConnectors(this IServiceCollection services, IConfiguration configuration)
    {
        var allowPrivate = bool.TryParse(configuration[EgressGuard.ConfigKey], out var allow) && allow;
        foreach (var client in new[]
                 {
                     HttpRestConnectorHandlerBase.HttpClientName, SlackHandlerBase.HttpClientName,
                     TeamsSendMessageHandler.HttpClientName, GitHubHandlerBase.HttpClientName,
                     WebhookEmitHandler.HttpClientName, Microsoft365HandlerBase.HttpClientName,
                 })
        {
            services.AddHttpClient(client).ConfigurePrimaryHttpMessageHandler(() => EgressGuard.CreateHandler(allowPrivate));
        }
        services.TryAddScoped<ConnectorCallContext>();
        services.TryAddScoped<ConnectorActionExecutor>();
        foreach (var handler in new[]
                 {
                     typeof(HttpGetHandler), typeof(HttpPostHandler), typeof(HttpPutHandler), typeof(HttpPatchHandler),
                     typeof(HttpDeleteHandler), typeof(HttpHeadHandler), typeof(SlackSendMessageHandler), typeof(SlackSendDmHandler),
                     typeof(SlackPostApprovalMessageHandler), typeof(TeamsSendMessageHandler), typeof(TeamsSendWebhookMessageHandler),
                     typeof(TeamsPostAdaptiveCardHandler), typeof(EmailSendHandler), typeof(GitHubCreateIssueHandler),
                     typeof(GitHubGetIssueHandler), typeof(GitHubCreatePrHandler), typeof(GitHubAddCommentHandler),
                     typeof(PostgreSqlQueryHandler), typeof(PostgreSqlExecuteHandler), typeof(MySqlQueryHandler),
                     typeof(MySqlExecuteHandler), typeof(WebhookEmitHandler), typeof(ScheduleCronValidateHandler),
                     typeof(FileSystemReadHandler), typeof(FileSystemWriteHandler), typeof(FileSystemListHandler),
                     typeof(ScriptShellRunHandler), typeof(Microsoft365SendMailHandler), typeof(Microsoft365CreateEventHandler),
                     typeof(Microsoft365UploadFileHandler),
                 })
        {
            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IConnectorOperationHandler), handler));
        }
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
