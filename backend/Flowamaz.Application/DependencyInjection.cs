using Flowamaz.Application.Analytics;
using Flowamaz.Application.Auth.Services;
using Flowamaz.Application.Connectors;
using Flowamaz.Application.Connectors.Handlers;
using Flowamaz.Application.Connectors.Services;
using Flowamaz.Application.Platform.Services;
using Flowamaz.Application.Workflow.Creation;
using Flowamaz.Application.Workflow.Dna;
using Flowamaz.Application.Workflow.Empathy;
using Flowamaz.Application.Workflow.Debugger;
using Flowamaz.Application.Workflow.Interpreter;
using Flowamaz.Application.Workflow.Orchestrator;
using Flowamaz.Application.Workflow.Saga;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Application.Workflow.Validation;
using Flowamaz.Application.Workflow.Workers;
using Flowamaz.Application.Workspace.Services;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Workflow;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Application;

/// <summary>
/// Composition root for the Application layer. Registers application services and scans this
/// assembly for FluentValidation validators. Api.Program calls <see cref="AddApplication"/> once.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IOrganisationService, OrganisationService>();
        services.AddScoped<IOrgUserService, OrgUserService>();
        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddScoped<IWorkspaceMemberService, WorkspaceMemberService>();
        services.AddScoped<IWorkspaceApiKeyService, WorkspaceApiKeyService>();
        services.AddScoped<IWorkspaceAuthorizationService, WorkspaceAuthorizationService>();
        services.AddScoped<AuthService>();

        // Workflow engine (prompt 02-02). SfgParser is stateless → singleton.
        services.AddSingleton<SfgParser>();
        services.AddScoped<IWorkflowOrchestrator, WorkflowOrchestrator>();

        // Node workers + saga engine (prompt 02-03).
        services.AddHttpClient(HttpActionWorker.HttpClientName);
        services.AddScoped<INodeWorker, HttpActionWorker>();

        // AI node worker + BYOM provider (prompt 04-03).
        services.AddHttpClient(ByomProviderService.HttpClientName);
        services.AddScoped<IByomProviderService, ByomProviderService>();
        services.AddScoped<INodeWorker, AiNodeWorker>();

        // Human gate node worker (prompt 04-04).
        services.AddScoped<INodeWorker, HumanGateNodeWorker>();

        services.AddScoped<INodeWorkerRegistry, NodeWorkerRegistry>();
        services.AddScoped<ISagaEngine, SagaEngine>();

        // Workflow API services (prompt 02-04).
        services.AddScoped<WorkflowService>();
        services.AddScoped<InstanceService>();
        services.AddScoped<GateService>();

        // Interpreter + step debugger (prompt 02-05).
        services.AddScoped<IWorkflowInterpreterService, WorkflowInterpreterService>();
        services.AddScoped<IStepDebuggerService, StepDebuggerService>();

        // Process Intelligence + Workflow Weather (prompt 02-07).
        services.AddScoped<ProcessIntelligenceService>();
        services.AddScoped<WorkflowWeatherService>();
        services.AddScoped<InsightService>();

        // Workflow validator (prompt 03-01).
        services.AddScoped<IWorkflowValidator, WorkflowValidator>();

        // NL→YAML generation + Co-pilot pattern matcher (prompt 03-03).
        services.AddScoped<INlYamlGenerationService, NlYamlGenerationService>();
        services.AddSingleton<ICopilotPatternMatcher, CopilotPatternMatcher>();

        // Visual input + Conversation import (prompt 03-04).
        services.AddScoped<IVisualInputService, VisualInputService>();
        services.AddScoped<IConversationImportService, ConversationImportService>();

        // Co-pilot full service + SOP parsing (prompt 03-05).
        services.AddScoped<ICopilotService, CopilotService>();
        services.AddScoped<ISopParsingService, SopParsingService>();

        // Workflow DNA service (prompt 03-07).
        services.AddScoped<IWorkflowDnaService, WorkflowDnaService>();

        // Workflow Empathy service (prompt 04-06).
        services.AddScoped<IWorkflowEmpathyService, WorkflowEmpathyService>();

        // Payload auto-mapper (prompt 04-07).
        services.AddScoped<IPayloadAutoMapper, PayloadAutoMapper>();

        // Connector operation handlers (prompt 04-02) — registered as IConnectorOperationHandler for registry resolution.
        services.AddHttpClient("http-rest-connector");
        services.AddHttpClient("slack-connector");
        services.AddHttpClient("teams-connector");
        services.AddHttpClient("github-connector");
        services.AddHttpClient("webhook-emit-connector");

        services.AddScoped<IConnectorOperationHandler, HttpGetHandler>();
        services.AddScoped<IConnectorOperationHandler, HttpPostHandler>();
        services.AddScoped<IConnectorOperationHandler, HttpPutHandler>();
        services.AddScoped<IConnectorOperationHandler, HttpPatchHandler>();
        services.AddScoped<IConnectorOperationHandler, HttpDeleteHandler>();
        services.AddScoped<IConnectorOperationHandler, HttpHeadHandler>();
        services.AddScoped<IConnectorOperationHandler, SlackSendMessageHandler>();
        services.AddScoped<IConnectorOperationHandler, SlackSendDmHandler>();
        services.AddScoped<IConnectorOperationHandler, SlackPostApprovalMessageHandler>();
        services.AddScoped<IConnectorOperationHandler, TeamsSendMessageHandler>();
        services.AddScoped<IConnectorOperationHandler, TeamsPostAdaptiveCardHandler>();
        services.AddScoped<IConnectorOperationHandler, EmailSendHandler>();
        services.AddScoped<IConnectorOperationHandler, GitHubCreateIssueHandler>();
        services.AddScoped<IConnectorOperationHandler, GitHubGetIssueHandler>();
        services.AddScoped<IConnectorOperationHandler, GitHubCreatePrHandler>();
        services.AddScoped<IConnectorOperationHandler, GitHubAddCommentHandler>();
        services.AddScoped<IConnectorOperationHandler, PostgreSqlQueryHandler>();
        services.AddScoped<IConnectorOperationHandler, PostgreSqlExecuteHandler>();
        services.AddScoped<IConnectorOperationHandler, WebhookEmitHandler>();
        services.AddScoped<IConnectorOperationHandler, ScheduleCronValidateHandler>();
        services.AddScoped<IConnectorOperationHandler, MySqlQueryHandler>();
        services.AddScoped<IConnectorOperationHandler, MySqlExecuteHandler>();

        services.AddScoped<ConnectorOperationHandlerRegistry>();
        services.AddScoped<IConnectorOperationHandlerRegistry>(sp => sp.GetRequiredService<ConnectorOperationHandlerRegistry>());
        services.AddHttpClient(OAuthService.HttpClientName);
        services.AddScoped<IOAuthService, OAuthService>();
        services.AddScoped<IConnectorHealthService, ConnectorHealthService>();

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        return services;
    }
}
