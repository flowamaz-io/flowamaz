using Flowamaz.Core.Configuration;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Persistence.Repositories;
using Flowamaz.Infrastructure.Queue;
using Flowamaz.Infrastructure.Services;
using Flowamaz.Infrastructure.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Flowamaz.Infrastructure;

/// <summary>
/// Composition root for Infrastructure. Api.Program calls <see cref="AddInfrastructure"/> once
/// during startup; no other layer instantiates Infrastructure services directly.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        AddOptions(services, configuration);
        AddPersistence(services, configuration);
        AddRepositories(services);
        AddStartupMigration(services);
        AddWorkflowEngine(services, configuration);
        AddRedis(services, configuration);
        AddAiServices(services);
        AddAuthServices(services);
        AddEmail(services);
        return services;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = ConnectionStringResolver.ResolveDatabase(configuration);

        services.AddDbContext<FlowAmazDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"));
            options.UseLazyLoadingProxies(false); // explicit Include — keeps queries predictable
        });
    }

    private static void AddRepositories(IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<IOrganisationRepository, OrganisationRepository>();
        services.AddScoped<IOrgUserRepository, OrgUserRepository>();
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<IWorkspaceMemberRepository, WorkspaceMemberRepository>();
        services.AddScoped<IWorkspaceApiKeyRepository, WorkspaceApiKeyRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        services.AddScoped<IWorkflowDefinitionRepository, WorkflowDefinitionRepository>();
        services.AddScoped<IWorkflowVersionRepository, WorkflowVersionRepository>();
        services.AddScoped<IWorkflowInstanceRepository, WorkflowInstanceRepository>();
        services.AddScoped<IWorkflowNodeStateRepository, WorkflowNodeStateRepository>();
        services.AddScoped<IWorkflowVariableRepository, WorkflowVariableRepository>();
        services.AddScoped<IWorkflowEventRepository, WorkflowEventRepository>();
        services.AddScoped<IGateDecisionRepository, GateDecisionRepository>();
    }

    private static void AddWorkflowEngine(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ITaskQueue, RedisTaskQueue>();
        services.AddScoped<IVariableEvaluationService, VariableEvaluationService>();

        // The background worker can be switched off (Worker:Enabled=false) — used by integration
        // tests so the live polling loop doesn't race API writes (the full loop lands in 02-08).
        if (configuration.GetValue<bool?>("Worker:Enabled") ?? true)
        {
            services.AddHostedService<OrchestratorWorker>();
        }

        // Interpreter AI completion + step-debugger state store (prompt 02-05).
        services.AddSingleton<IAiCompletionService, AiCompletionService>();
        services.AddSingleton<IDebugStateStore, RedisDebugStateStore>();
    }

    private static void AddStartupMigration(IServiceCollection services)
    {
        services.AddScoped<IMigrationRunner, EfMigrationRunner>();
        services.AddScoped<IStartupMigrationService, StartupMigrationService>();
    }

    private static void AddRedis(IServiceCollection services, IConfiguration configuration)
    {
        var redisConnection = ConnectionStringResolver.ResolveRedis(configuration);

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConnection));
    }

    private static void AddAiServices(IServiceCollection services)
    {
        services.AddScoped<IModelResolutionService, ModelResolutionService>();
        services.AddScoped<IWorkspaceAiConfigService, WorkspaceAiConfigService>();
        services.AddSingleton<IRateLimitService, RateLimitService>();
        services.AddSingleton<ISemanticCacheService, SemanticCacheService>();
        services.AddSingleton<IAiTokenMeteringService, AiTokenMeteringService>();
    }

    private static void AddAuthServices(IServiceCollection services)
    {
        services.AddSingleton<IJwtService, JwtService>();
    }

    private static void AddEmail(IServiceCollection services)
    {
        services.AddHttpClient(EmailService.HttpClientName);
        services.AddSingleton<IEmailService, EmailService>();
    }
}
