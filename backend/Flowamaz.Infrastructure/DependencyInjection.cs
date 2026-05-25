using Flowamaz.Core.Configuration;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Persistence.Repositories;
using Flowamaz.Infrastructure.Services;
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
