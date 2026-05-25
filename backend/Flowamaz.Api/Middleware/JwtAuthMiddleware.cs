using System.IdentityModel.Tokens.Jwt;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;

namespace Flowamaz.Api.Middleware;

/// <summary>
/// Resolves the request identity from the Authorization header into
/// <c>HttpContext.Items["CurrentUser"]</c> so the current-user service can read it. Two paths:
/// a Bearer JWT (human) or a token with the <c>fmz_</c> prefix (workspace API key). Requests with
/// neither proceed unauthenticated — controllers decide what to require. Exempt: /api/v1/auth/*,
/// /scalar, /health, /openapi.
/// </summary>
public sealed class JwtAuthMiddleware
{
    private const string ApiKeyPrefix = "fmz_";
    private const string BearerPrefix = "Bearer ";
    private static readonly string[] ExemptPrefixes = ["/api/v1/auth", "/scalar", "/health", "/openapi"];

    private readonly RequestDelegate _next;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JwtAuthMiddleware> _logger;

    public JwtAuthMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory, ILogger<JwtAuthMiddleware> logger)
    {
        _next = next;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IJwtService jwtService, IWorkspaceApiKeyService apiKeyService)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (IsExempt(path))
        {
            await _next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var token = header[BearerPrefix.Length..].Trim();
            if (token.StartsWith(ApiKeyPrefix, StringComparison.Ordinal))
            {
                await ResolveApiKeyAsync(context, apiKeyService, token);
            }
            else
            {
                ResolveJwt(context, jwtService, token);
            }
        }

        await _next(context);
    }

    private void ResolveJwt(HttpContext context, IJwtService jwtService, string token)
    {
        var principal = jwtService.ValidateAccessToken(token);
        if (principal is null) return;

        context.Items[CurrentUserContext.HttpContextItemKey] = new CurrentUserContext
        {
            OrgUserId = jwtService.ExtractOrgUserId(principal),
            OrgId = jwtService.ExtractOrgId(principal),
            OrgSlug = principal.FindFirst("org_slug")?.Value,
            Email = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value,
            Name = principal.FindFirst("name")?.Value,
            IsOrgOwner = principal.FindFirst("is_org_owner")?.Value == "true",
            IsApiKey = false,
            WorkspaceMemberships = jwtService.ExtractWorkspaceMemberships(principal),
        };
    }

    private async Task ResolveApiKeyAsync(HttpContext context, IWorkspaceApiKeyService apiKeyService, string token)
    {
        var validation = await apiKeyService.ValidateApiKeyAsync(token);
        if (validation is null) return;

        context.Items[CurrentUserContext.HttpContextItemKey] = new CurrentUserContext
        {
            OrgUserId = Guid.Empty,
            OrgId = Guid.Empty,
            IsApiKey = true,
            ApiKeyWorkspaceId = validation.WorkspaceId,
            ApiKeyEnvironmentId = validation.EnvironmentId,
            ApiKeyScopes = validation.Scopes,
        };

        // Last-used bookkeeping must not block the request — run it in a detached scope.
        RecordLastUsedFireAndForget(validation.KeyId);
    }

    private void RecordLastUsedFireAndForget(Guid keyId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IWorkspaceApiKeyService>();
                await service.RecordLastUsedAsync(keyId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "JwtAuthMiddleware.RecordLastUsedFireAndForget error keyId={KeyId}", keyId);
            }
        });
    }

    private static bool IsExempt(string path) =>
        ExemptPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
