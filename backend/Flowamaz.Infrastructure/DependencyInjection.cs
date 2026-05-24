using Flowamaz.Core.Configuration;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Identity;
using Flowamaz.Infrastructure.Persistence;
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
        AddRedis(services, configuration);
        AddAiServices(services);
        AddEmail(services);
        AddIdentityStubs(services);
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
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is required (set DB_CONNECTION_STRING env var).");

        services.AddDbContext<FlowAmazDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"));
            options.UseLazyLoadingProxies(false); // explicit Include — keeps queries predictable
        });
    }

    private static void AddRedis(IServiceCollection services, IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Redis is required (set REDIS_CONNECTION_STRING env var).");

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConnection));
    }

    private static void AddAiServices(IServiceCollection services)
    {
        services.AddScoped<IModelResolutionService, ModelResolutionService>();
        services.AddSingleton<IRateLimitService, RateLimitService>();
        services.AddSingleton<ISemanticCacheService, SemanticCacheService>();
        services.AddSingleton<IAiTokenMeteringService, AiTokenMeteringService>();
    }

    private static void AddEmail(IServiceCollection services)
    {
        services.AddHttpClient(EmailService.HttpClientName);
        services.AddSingleton<IEmailService, EmailService>();
    }

    private static void AddIdentityStubs(IServiceCollection services)
    {
        // Anonymous default until real JWT-backed ICurrentUserService lands in prompt 04.
        services.TryAddScoped<ICurrentUserService, AnonymousCurrentUserService>();
    }
}
