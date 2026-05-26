using Flowamaz.Api.Authorization;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Flowamaz.Core.Entities.Workflow;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/workflows")]
public sealed class WorkflowDnaController : ControllerBase
{
    private readonly IWorkflowDnaService _dna;
    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<WorkflowDnaController> _log;

    public WorkflowDnaController(
        IWorkflowDnaService dna,
        IWorkflowDefinitionRepository definitions,
        IUnitOfWork uow,
        ILogger<WorkflowDnaController> log)
    {
        _dna = dna;
        _definitions = definitions;
        _uow = uow;
        _log = log;
    }

    /// <summary>Compute the DNA fingerprint for a workflow.</summary>
    [HttpGet("{id:guid}/dna")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<WorkflowDna>> GetDna(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        _log.LogInformation("WorkflowDnaController.GetDna entry workspaceId={WorkspaceId} id={Id}", workspaceId, id);
        var result = await _dna.ComputeDnaAsync(id, workspaceId, cancellationToken);
        _log.LogInformation("WorkflowDnaController.GetDna exit hash={Hash}", result.DnaHash);
        return Ok(result);
    }

    /// <summary>Find workflows similar to the given one. Returns up to 10 results by similarity score.</summary>
    [HttpGet("{id:guid}/similar")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<IReadOnlyList<WorkflowSimilarity>>> GetSimilar(
        Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        _log.LogInformation("WorkflowDnaController.GetSimilar entry workspaceId={WorkspaceId} id={Id}", workspaceId, id);
        var dna = await _dna.ComputeDnaAsync(id, workspaceId, cancellationToken);
        var similar = await _dna.FindSimilarAsync(workspaceId, dna, limit: 10, cancellationToken);
        _log.LogInformation("WorkflowDnaController.GetSimilar exit count={Count}", similar.Count);
        return Ok(similar);
    }

    /// <summary>Suggest a clone when the user is typing a new workflow name.</summary>
    [HttpGet("suggest-clone")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<CloneSuggestion?>> SuggestClone(
        Guid workspaceId, [FromQuery] string name, CancellationToken cancellationToken)
    {
        _log.LogInformation("WorkflowDnaController.SuggestClone entry workspaceId={WorkspaceId} name={Name}", workspaceId, name);
        var suggestion = await _dna.SuggestCloneAsync(workspaceId, name, cancellationToken);
        _log.LogInformation("WorkflowDnaController.SuggestClone exit found={Found}", suggestion is not null);
        return Ok(suggestion);
    }

    /// <summary>
    /// Clone a workflow. Creates a new Draft copy with the same YAML.
    /// The clone name is "{original_name} (copy)".
    /// </summary>
    [HttpPost("{id:guid}/clone")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<WorkflowDefinitionSummary>> Clone(
        Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        _log.LogInformation("WorkflowDnaController.Clone entry workspaceId={WorkspaceId} id={Id}", workspaceId, id);

        var source = await _definitions.GetByIdForWorkspaceAsync(id, workspaceId, cancellationToken);
        if (source is null)
            return NotFound(new { error = $"Workflow {id} not found in this workspace." });

        var clone = new WorkflowDefinition
        {
            WorkspaceId = workspaceId,
            Name = $"{source.Name} (copy)",
            Slug = $"{source.Slug}-copy-{Guid.NewGuid().ToString("N")[..6]}",
            Description = source.Description,
            YamlContent = source.YamlContent,
            NlDescription = source.NlDescription,
            CreatedByMethod = WorkflowCreatedByMethod.Canvas,
            Status = WorkflowStatus.Draft,
            TriggerType = source.TriggerType,
            CurrentVersion = "0.0.1",
        };

        await _definitions.AddAsync(clone, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        _log.LogInformation("WorkflowDnaController.Clone exit cloneId={CloneId}", clone.Id);
        return Created($"/api/v1/workspaces/{workspaceId}/workflows/{clone.Id}",
            new WorkflowDefinitionSummary(clone.Id, clone.Name, clone.Slug));
    }
}

public sealed record WorkflowDefinitionSummary(Guid Id, string Name, string Slug);
