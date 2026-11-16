using Flowamaz.Engine.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class EngineApiServiceCollectionExtensions
{
    /// <summary>
    /// Settings (section <c>EngineApi</c>) and the default webhook secret (<see cref="ConfiguredWebhookSecret"/>). Call it
    /// with <c>AddFlowamazEngineRuntime</c> and <c>AddFlowamazEnginePersistence</c>, register an <see cref="IEngineApiAccess"/>,
    /// then <c>app.MapFlowamazEngineApi()</c>.
    /// </summary>
    public static IServiceCollection AddFlowamazEngineApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EngineApiOptions>()
            .Bind(configuration.GetSection(EngineApiOptions.Section))
            .Validate(o => string.IsNullOrEmpty(o.WebhookSecret) || o.WebhookSecret.Length >= 32,
                "EngineApi:WebhookSecret must be at least 32 characters. Generate one (e.g. openssl rand -hex 32) and keep it in a secret store.")
            .Validate(o => o.MaxWebhookBodyBytes > 0 && o.MaxPageSize > 0,
                "EngineApi:MaxWebhookBodyBytes and EngineApi:MaxPageSize must be greater than 0.")
            .ValidateOnStart();
        services.TryAddSingleton<IWebhookSecretResolver, ConfiguredWebhookSecret>();
        return services;
    }
}
