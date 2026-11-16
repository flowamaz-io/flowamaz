using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Runtime;
using Flowamaz.Infrastructure.Persistence.Ai;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the runtime's storage: <see cref="EngineDbContext"/>, the repositories and the unit of work.</summary>
public static class EnginePersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="EngineDbContext"/> (configured by <paramref name="configure"/>), the 7 run
    /// repositories, <see cref="IUnitOfWork"/> and the AI usage meter (<see cref="EfAiUsageMeter"/>), all
    /// scoped so one scope shares one context.
    /// PostgreSQL: <c>o =&gt; o.UseNpgsql(connectionString, EngineDbContextOptions.ConfigureNpgsql)</c>, then apply
    /// the migrations. EF Core's in-memory provider also works, for one host process (tests, trying it out).
    /// </summary>
    public static IServiceCollection AddFlowamazEnginePersistence(
        this IServiceCollection services, Action<DbContextOptionsBuilder> configure)
    {
        services.AddDbContext<EngineDbContext>(configure);
        services.AddFlowamazEngineRepositories();
        // AI usage (required by the AI step): engine_ai_usage. Register another IAiUsageMeter to send it elsewhere.
        services.TryAddScoped<IAiUsageMeter, EfAiUsageMeter>();
        return services;
    }

    /// <summary>
    /// Adds the repositories and unit of work only, for a host that registers <see cref="EngineDbContext"/>
    /// itself (for example its own derived context, resolved as <see cref="EngineDbContext"/>).
    /// </summary>
    public static IServiceCollection AddFlowamazEngineRepositories(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IWorkflowDefinitionRepository, WorkflowDefinitionRepository>();
        services.AddScoped<IWorkflowVersionRepository, WorkflowVersionRepository>();
        services.AddScoped<IWorkflowInstanceRepository, WorkflowInstanceRepository>();
        services.AddScoped<IWorkflowNodeStateRepository, WorkflowNodeStateRepository>();
        services.AddScoped<IWorkflowVariableRepository, WorkflowVariableRepository>();
        services.AddScoped<IWorkflowEventRepository, WorkflowEventRepository>();
        services.AddScoped<IGateDecisionRepository, GateDecisionRepository>();
        return services;
    }
}
