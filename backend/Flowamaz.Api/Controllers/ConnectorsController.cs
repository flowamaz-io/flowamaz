using System.Text.Json;
using Flowamaz.Api.Authorization;
using Flowamaz.Application.Connectors.Services;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Connector catalogue, installation, health, and OAuth initiation.
/// Read operations require Viewer; write operations require Designer.
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/connectors")]
public sealed class ConnectorsController : ControllerBase
{
    private readonly IConnectorCatalogueService _catalogue;
    private readonly IConnectorHealthService _health;
    private readonly IOAuthService _oauth;
    private readonly ICurrentUserService _currentUser;
    private readonly IPayloadAutoMapper _autoMapper;

    public ConnectorsController(
        IConnectorCatalogueService catalogue,
        IConnectorHealthService health,
        IOAuthService oauth,
        ICurrentUserService currentUser,
        IPayloadAutoMapper autoMapper)
    {
        _catalogue = catalogue;
        _health = health;
        _oauth = oauth;
        _currentUser = currentUser;
        _autoMapper = autoMapper;
    }

    /// <summary>List all connectors in the catalogue.</summary>
    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    // Catalogue changes only when an admin seeds new connectors — cache for an hour, keyed per caller.
    [ResponseCache(Duration = 3600, VaryByHeader = "Authorization")]
    public async Task<IActionResult> List(Guid workspaceId, CancellationToken cancellationToken)
    {
        var connectors = await _catalogue.GetAllAsync(cancellationToken);
        return Ok(connectors);
    }

    /// <summary>Get connector definition detail by ConnectorId slug.</summary>
    [HttpGet("{connectorId}")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<IActionResult> GetById(Guid workspaceId, string connectorId, CancellationToken cancellationToken)
    {
        var all = await _catalogue.GetAllAsync(cancellationToken);
        var connector = all.FirstOrDefault(c => c.ConnectorId == connectorId);
        return connector is null ? NotFound() : Ok(connector);
    }

    /// <summary>Install a connector into the workspace.</summary>
    [HttpPost("{connectorId}/install")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<IActionResult> Install(
        Guid workspaceId,
        string connectorId,
        [FromBody] InstallConnectorRequest request,
        CancellationToken cancellationToken)
    {
        var all = await _catalogue.GetAllAsync(cancellationToken);
        var def = all.FirstOrDefault(c => c.ConnectorId == connectorId);
        if (def is null) return NotFound($"Connector '{connectorId}' not found in catalogue.");

        var installed = await _catalogue.InstallAsync(
            workspaceId, def.Id, request.CredentialId, _currentUser.UserId!.Value, cancellationToken);
        return Ok(installed);
    }

    /// <summary>Uninstall a connector from the workspace.</summary>
    [HttpDelete("{workspaceConnectorId:guid}/uninstall")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<IActionResult> Uninstall(
        Guid workspaceId,
        Guid workspaceConnectorId,
        CancellationToken cancellationToken)
    {
        await _catalogue.UninstallAsync(workspaceId, workspaceConnectorId, cancellationToken);
        return NoContent();
    }

    /// <summary>List all connectors installed in the workspace.</summary>
    [HttpGet("installed")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<IActionResult> GetInstalled(Guid workspaceId, CancellationToken cancellationToken)
    {
        var installed = await _catalogue.GetInstalledAsync(workspaceId, cancellationToken);
        return Ok(installed);
    }

    /// <summary>Health status for all installed connectors in the workspace.</summary>
    [HttpGet("health")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<IActionResult> GetHealth(Guid workspaceId, CancellationToken cancellationToken)
    {
        var health = await _health.GetHealthAsync(workspaceId, cancellationToken);
        return Ok(health);
    }

    /// <summary>Initiate OAuth flow for a connector that supports it.</summary>
    [HttpPost("{connectorId}/oauth/initiate")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<IActionResult> InitiateOAuth(
        Guid workspaceId,
        string connectorId,
        CancellationToken cancellationToken)
    {
        var result = await _oauth.InitiateAsync(workspaceId, connectorId, cancellationToken);
        return Ok(new { result.AuthorizationUrl, result.State });
    }

    /// <summary>Auto-map a sample payload to a connector operation's input schema fields.</summary>
    [HttpPost("{connectorId}/operations/{operationId}/auto-map")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<IActionResult> AutoMap(
        Guid workspaceId,
        string connectorId,
        string operationId,
        [FromBody] AutoMapRequest request,
        CancellationToken cancellationToken)
    {
        // Build target schema: use provided or look it up from the connector manifest
        JsonElement targetSchema;
        if (request.TargetSchema.HasValue)
        {
            targetSchema = request.TargetSchema.Value;
        }
        else
        {
            var all = await _catalogue.GetAllAsync(cancellationToken);
            var def = all.FirstOrDefault(c => c.ConnectorId == connectorId);
            if (def is null)
                return NotFound($"Connector '{connectorId}' not found in catalogue.");

            try
            {
                using var manifestDoc = JsonDocument.Parse(def.ManifestJson);
                if (!manifestDoc.RootElement.TryGetProperty("operations", out var ops)
                    || ops.ValueKind != JsonValueKind.Array)
                    return BadRequest("Connector manifest has no operations array.");

                JsonElement? opSchema = null;
                foreach (var op in ops.EnumerateArray())
                {
                    var opId = op.TryGetProperty("id", out var idProp) ? idProp.GetString()
                        : op.TryGetProperty("name", out var nameProp) ? nameProp.GetString()
                        : null;
                    if (string.Equals(opId, operationId, StringComparison.OrdinalIgnoreCase))
                    {
                        opSchema = op.TryGetProperty("input_schema", out var schema) ? schema : default(JsonElement?);
                        break;
                    }
                }

                if (opSchema is null)
                    return NotFound($"Operation '{operationId}' not found in connector '{connectorId}'.");

                targetSchema = opSchema.Value;
            }
            catch (JsonException)
            {
                return BadRequest("Connector manifest JSON is invalid.");
            }
        }

        using var sampleDoc = JsonDocument.Parse(request.SamplePayload.GetRawText());
        var result = await _autoMapper.MapAsync(sampleDoc, targetSchema, workspaceId, cancellationToken);
        return Ok(result);
    }
}

public record InstallConnectorRequest(Guid? CredentialId);

public record AutoMapRequest(
    JsonElement SamplePayload,
    JsonElement? TargetSchema = null
);
