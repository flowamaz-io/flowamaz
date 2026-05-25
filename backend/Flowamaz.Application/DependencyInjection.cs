using Flowamaz.Application.Platform.Services;
using Flowamaz.Application.Workspace.Services;
using Flowamaz.Core.Interfaces.Services;
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
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        return services;
    }
}
