using System.Security.Claims;
using Flowamaz.Application.Workflow.DTOs;
using Microsoft.AspNetCore.Http;

namespace Flowamaz.Engine.Api;

/// <summary>What a request wants to do, for <see cref="IEngineApiAccess"/> to allow or refuse.</summary>
public enum EngineApiAction
{
    /// <summary>List and open workflows, versions, runs (without their data unless the caller's <see cref="RunAccess"/> allows) and gates.</summary>
    Read,
    /// <summary>Create workflows, save drafts, delete drafts.</summary>
    Author,
    /// <summary>Publish, archive and unarchive.</summary>
    Publish,
    /// <summary>Start runs (and test runs).</summary>
    Run,
    /// <summary>Cancel and retry runs.</summary>
    Operate,
    /// <summary>Approve or reject a gate.</summary>
    Decide,
}

/// <param name="UserId">Recorded as the author, publisher, trigger or decider.</param>
/// <param name="Runs">What the caller may see of runs: everything, runs without their data, or only the runs they started.</param>
public sealed record EngineCaller(Guid UserId, RunAccess Runs)
{
    public static EngineCaller Full(Guid userId) => new(userId, RunAccess.Full);
}

/// <summary>
/// The host's access rule for the engine endpoints: who the caller is and whether they may do
/// <paramref name="action"/> in <paramref name="workspaceId"/>. Return null to refuse (401 when the request isn't
/// signed in, 403 otherwise). It is required: <c>AddFlowamazEngineApi</c> registers none, so the host has to decide.
/// </summary>
public interface IEngineApiAccess
{
    Task<EngineCaller?> AuthorizeAsync(HttpContext http, Guid workspaceId, EngineApiAction action, CancellationToken ct);
}

/// <summary>
/// For a single-team host: any signed-in user may do anything in <see cref="WorkspaceId"/>, and nothing in any other
/// workspace. The user id comes from the <c>sub</c> or name-identifier claim, which must be a GUID. Register it
/// explicitly (<c>services.AddSingleton&lt;IEngineApiAccess&gt;(new SignedInUserAccess(workspaceId))</c>).
/// </summary>
public sealed class SignedInUserAccess : IEngineApiAccess
{
    public SignedInUserAccess(Guid workspaceId) => WorkspaceId = workspaceId;

    public Guid WorkspaceId { get; }

    public Task<EngineCaller?> AuthorizeAsync(HttpContext http, Guid workspaceId, EngineApiAction action, CancellationToken ct)
    {
        var user = http.User;
        if (workspaceId != WorkspaceId || user.Identity?.IsAuthenticated != true) return Task.FromResult<EngineCaller?>(null);
        var id = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Task.FromResult(Guid.TryParse(id, out var userId) ? EngineCaller.Full(userId) : null);
    }
}

/// <summary>
/// The secret a workflow's webhook requests are signed with (HMAC-SHA256 of the body). Return null when the workflow
/// takes no webhooks. The default (<see cref="ConfiguredWebhookSecret"/>) uses one secret for the whole host.
/// </summary>
public interface IWebhookSecretResolver
{
    Task<string?> GetSecretAsync(Guid workspaceId, Guid workflowId, CancellationToken ct);
}

/// <summary>Default: <see cref="EngineApiOptions.WebhookSecret"/> for every workflow; no secret, no webhooks.</summary>
public sealed class ConfiguredWebhookSecret : IWebhookSecretResolver
{
    private readonly string? _secret;

    public ConfiguredWebhookSecret(Microsoft.Extensions.Options.IOptions<EngineApiOptions> options) => _secret = options.Value.WebhookSecret;

    public Task<string?> GetSecretAsync(Guid workspaceId, Guid workflowId, CancellationToken ct) =>
        Task.FromResult(string.IsNullOrWhiteSpace(_secret) ? null : _secret);
}

/// <summary>Settings (configuration section <c>EngineApi</c>).</summary>
public sealed class EngineApiOptions
{
    public const string Section = "EngineApi";

    /// <summary>Signs webhook requests for every workflow (at least 32 characters; keep it in a secret store). Unset: webhooks are off.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Largest webhook body accepted, in bytes (default 1 MB).</summary>
    public int MaxWebhookBodyBytes { get; set; } = 1_048_576;

    /// <summary>Largest page a list returns (default 100).</summary>
    public int MaxPageSize { get; set; } = 100;
}
