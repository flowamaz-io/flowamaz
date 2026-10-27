using System.Text.Json;
using System.Text.Json.Nodes;
using Flowamaz.Application.Auth.Scim;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// SCIM 2.0 service provider (RFC 7644) at <c>/api/scim/v2</c>, for identity providers (Okta, Microsoft Entra,
/// OneLogin…) to provision an Enterprise organisation's users and groups. Authenticated only by an organisation's SCIM
/// bearer token (never a user session), rate limited per organisation, and answered in SCIM's own JSON and error
/// format (the internal response wrapper skips this path).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/scim/v2")]
public sealed class ScimController : ControllerBase
{
    private const string ScimContentType = "application/scim+json";
    private const int MaxBodyBytes = 1_000_000;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly ScimService _scim;
    private readonly IRateLimitService _rateLimit;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ScimController> _logger;

    public ScimController(ScimService scim, IRateLimitService rateLimit, IConfiguration configuration, ILogger<ScimController> logger)
    {
        _scim = scim;
        _rateLimit = rateLimit;
        _configuration = configuration;
        _logger = logger;
    }

    // ── Discovery (RFC 7644 §4) ─────────────────────────────────────────────────────────

    [HttpGet("ServiceProviderConfig")]
    public Task<IActionResult> ServiceProviderConfig(CancellationToken ct) => RunAsync(_ => Task.FromResult<IActionResult>(Scim(new JsonObject
    {
        ["schemas"] = new JsonArray("urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig"),
        ["documentationUri"] = "https://docs.flowamaz.io/sso/scim",
        ["patch"] = new JsonObject { ["supported"] = true },
        ["bulk"] = new JsonObject { ["supported"] = false, ["maxOperations"] = 0, ["maxPayloadSize"] = 0 },
        ["filter"] = new JsonObject { ["supported"] = true, ["maxResults"] = ScimJson.MaxPageSize },
        ["changePassword"] = new JsonObject { ["supported"] = false },
        ["sort"] = new JsonObject { ["supported"] = false },
        ["etag"] = new JsonObject { ["supported"] = false },
        ["authenticationSchemes"] = new JsonArray(new JsonObject
        {
            ["type"] = "oauthbearertoken",
            ["name"] = "Bearer token",
            ["description"] = "A SCIM token created in Flowamaz under Settings → SSO → SCIM provisioning.",
            ["primary"] = true,
        }),
    })), ct);

    [HttpGet("ResourceTypes")]
    public Task<IActionResult> ResourceTypes(CancellationToken ct) => RunAsync(_ =>
    {
        JsonNode Type(string name, string endpoint, string schema) => new JsonObject
        {
            ["schemas"] = new JsonArray("urn:ietf:params:scim:schemas:core:2.0:ResourceType"),
            ["id"] = name, ["name"] = name, ["endpoint"] = endpoint, ["schema"] = schema,
            ["meta"] = new JsonObject { ["resourceType"] = "ResourceType", ["location"] = $"{_scim.ScimBaseUrl}/ResourceTypes/{name}" },
        };
        return Task.FromResult<IActionResult>(Scim(ScimJson.List(
            [Type("User", "/Users", ScimJson.UserSchema), Type("Group", "/Groups", ScimJson.GroupSchema)], 2, 1)));
    }, ct);

    [HttpGet("Schemas")]
    public Task<IActionResult> Schemas(CancellationToken ct) => RunAsync(_ =>
    {
        JsonNode Attr(string name, string type = "string", bool required = false, bool multi = false, string uniqueness = "none") => new JsonObject
        {
            ["name"] = name, ["type"] = type, ["multiValued"] = multi, ["required"] = required,
            ["caseExact"] = false, ["mutability"] = name == "id" ? "readOnly" : "readWrite", ["returned"] = "default", ["uniqueness"] = uniqueness,
        };
        JsonNode Schema(string id, string name, params JsonNode[] attributes) => new JsonObject
        {
            ["schemas"] = new JsonArray("urn:ietf:params:scim:schemas:core:2.0:Schema"),
            ["id"] = id, ["name"] = name, ["attributes"] = new JsonArray(attributes),
            ["meta"] = new JsonObject { ["resourceType"] = "Schema", ["location"] = $"{_scim.ScimBaseUrl}/Schemas/{id}" },
        };
        return Task.FromResult<IActionResult>(Scim(ScimJson.List(
        [
            Schema(ScimJson.UserSchema, "User", Attr("userName", required: true, uniqueness: "server"), Attr("name", "complex"),
                Attr("displayName"), Attr("emails", "complex", multi: true), Attr("active", "boolean"), Attr("externalId")),
            Schema(ScimJson.GroupSchema, "Group", Attr("displayName", required: true, uniqueness: "server"),
                Attr("members", "complex", multi: true), Attr("externalId")),
        ], 2, 1)));
    }, ct);

    // ── Users ───────────────────────────────────────────────────────────────────────────

    [HttpGet("Users")]
    public Task<IActionResult> ListUsers([FromQuery] string? filter, [FromQuery] int? startIndex, [FromQuery] int? count, CancellationToken ct) =>
        RunAsync(async caller => Scim(await _scim.ListUsersAsync(caller.OrgId, filter, startIndex, count, ct)), ct);

    [HttpGet("Users/{id}")]
    public Task<IActionResult> GetUser(string id, CancellationToken ct) =>
        RunAsync(async caller => Scim(await _scim.GetUserAsync(caller.OrgId, Id(id, "user"), ct)), ct);

    [HttpPost("Users")]
    public Task<IActionResult> CreateUser(CancellationToken ct) =>
        RunAsync(async caller => Created(await _scim.CreateUserAsync(caller.OrgId, await BodyAsync(ct), ct)), ct);

    [HttpPut("Users/{id}")]
    public Task<IActionResult> ReplaceUser(string id, CancellationToken ct) =>
        RunAsync(async caller => Scim(await _scim.ReplaceUserAsync(caller.OrgId, Id(id, "user"), await BodyAsync(ct), ct)), ct);

    [HttpPatch("Users/{id}")]
    public Task<IActionResult> PatchUser(string id, CancellationToken ct) =>
        RunAsync(async caller => Scim(await _scim.PatchUserAsync(caller.OrgId, Id(id, "user"), await BodyAsync(ct), ct)), ct);

    [HttpDelete("Users/{id}")]
    public Task<IActionResult> DeleteUser(string id, CancellationToken ct) =>
        RunAsync(async caller => { await _scim.DeleteUserAsync(caller.OrgId, Id(id, "user"), ct); return NoContent(); }, ct);

    // ── Groups ──────────────────────────────────────────────────────────────────────────

    [HttpGet("Groups")]
    public Task<IActionResult> ListGroups([FromQuery] string? filter, [FromQuery] int? startIndex, [FromQuery] int? count,
        [FromQuery] string? excludedAttributes, CancellationToken ct) =>
        RunAsync(async caller => Scim(await _scim.ListGroupsAsync(caller.OrgId, filter, startIndex, count, IncludeMembers(excludedAttributes), ct)), ct);

    [HttpGet("Groups/{id}")]
    public Task<IActionResult> GetGroup(string id, [FromQuery] string? excludedAttributes, CancellationToken ct) =>
        RunAsync(async caller => Scim(await _scim.GetGroupAsync(caller.OrgId, Id(id, "group"), IncludeMembers(excludedAttributes), ct)), ct);

    [HttpPost("Groups")]
    public Task<IActionResult> CreateGroup(CancellationToken ct) =>
        RunAsync(async caller => Created(await _scim.CreateGroupAsync(caller.OrgId, await BodyAsync(ct), ct)), ct);

    [HttpPut("Groups/{id}")]
    public Task<IActionResult> ReplaceGroup(string id, CancellationToken ct) =>
        RunAsync(async caller => Scim(await _scim.ReplaceGroupAsync(caller.OrgId, Id(id, "group"), await BodyAsync(ct), ct)), ct);

    [HttpPatch("Groups/{id}")]
    public Task<IActionResult> PatchGroup(string id, CancellationToken ct) =>
        RunAsync(async caller => { await _scim.PatchGroupAsync(caller.OrgId, Id(id, "group"), await BodyAsync(ct), ct); return NoContent(); }, ct);

    [HttpDelete("Groups/{id}")]
    public Task<IActionResult> DeleteGroup(string id, CancellationToken ct) =>
        RunAsync(async caller => { await _scim.DeleteGroupAsync(caller.OrgId, Id(id, "group"), ct); return NoContent(); }, ct);

    // ── Plumbing ────────────────────────────────────────────────────────────────────────

    /// <summary>Authenticates the token, applies the organisation's rate limit, and turns refusals into SCIM errors.</summary>
    private async Task<IActionResult> RunAsync(Func<ScimCaller, Task<IActionResult>> action, CancellationToken ct)
    {
        var header = Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
        var caller = await _scim.AuthenticateAsync(token, ct);
        if (caller is null)
        {
            Response.Headers.WWWAuthenticate = "Bearer realm=\"Flowamaz SCIM\"";
            return Error(401, null, "The SCIM token is missing, wrong or revoked, or this organisation's plan doesn't include SSO. " +
                "Create a token in Flowamaz under Settings → SSO → SCIM provisioning and paste it into your identity provider.");
        }

        var perMinute = _configuration.GetValue("Scim:RequestsPerMinute", 600);
        if (!await _rateLimit.CheckAndIncrementAsync($"scim:{caller.OrgId}", perMinute, 60, ct))
        {
            Response.Headers.RetryAfter = "60";
            return Error(429, null, $"Too many SCIM requests: this organisation can make {perMinute} a minute. Your identity provider will retry.");
        }

        try
        {
            return await action(caller);
        }
        catch (ScimException ex)
        {
            _logger.LogInformation("SCIM request refused org={OrgId} {Method} {Path} status={Status} type={ScimType} detail={Detail}",
                caller.OrgId, Request.Method, Request.Path.Value, ex.HttpStatusCode, ex.ScimType, ex.Message);
            return Error(ex.HttpStatusCode, ex.ScimType, ex.Message);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        {
            // A unique index caught two requests creating the same user or group at once.
            _logger.LogWarning(ex, "SCIM write conflict org={OrgId} {Method} {Path}", caller.OrgId, Request.Method, Request.Path.Value);
            return Error(409, "uniqueness", "This change conflicts with another change made at the same time. Retry the request.");
        }
    }

    private async Task<string> BodyAsync(CancellationToken ct)
    {
        if (Request.ContentLength > MaxBodyBytes) throw ScimException.Invalid("The request body is too large (1 MB at most).");
        using var reader = new StreamReader(Request.Body);
        var buffer = new char[MaxBodyBytes + 1];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(), ct);
        if (read > MaxBodyBytes) throw ScimException.Invalid("The request body is too large (1 MB at most).");
        return new string(buffer, 0, read);
    }

    private static Guid Id(string id, string what) =>
        Guid.TryParse(id, out var guid) ? guid : throw ScimException.NotFound($"This {what}");

    private static bool IncludeMembers(string? excludedAttributes) =>
        excludedAttributes is null || !excludedAttributes.Split(',').Any(a => a.Trim().Equals("members", StringComparison.OrdinalIgnoreCase));

    private ContentResult Scim(JsonNode body, int status = 200) => new()
    {
        Content = body.ToJsonString(Json),
        ContentType = ScimContentType,
        StatusCode = status,
    };

    private IActionResult Created(JsonObject resource)
    {
        if (resource["meta"]?["location"]?.GetValue<string>() is { } location) Response.Headers.Location = location;
        return Scim(resource, 201);
    }

    private ContentResult Error(int status, string? scimType, string detail) => Scim(ScimJson.Error(status, scimType, detail), status);
}
