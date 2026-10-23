using Flowamaz.Api.Authorization;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Workflow Economy (FUNCTIONAL.md §8.9): publisher onboarding + earnings (org owner), listing prices,
/// and buying paid workflows for a workspace through Stripe Checkout. Stripe ids never leave the server.
/// Webhooks arrive on the existing <c>POST /api/v1/billing/webhook</c>.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class EconomyController : ControllerBase
{
    private readonly IWorkflowEconomyService _economy;
    private readonly IWorkflowTemplateRepository _templates;
    private readonly IWorkspaceRepository _workspaces;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<PublisherOnboardingRequest> _onboardingValidator;
    private readonly IValidator<TemplatePurchaseRequest> _purchaseValidator;
    private readonly ILogger<EconomyController> _logger;

    public EconomyController(
        IWorkflowEconomyService economy,
        IWorkflowTemplateRepository templates,
        IWorkspaceRepository workspaces,
        ICurrentUserService currentUser,
        IValidator<PublisherOnboardingRequest> onboardingValidator,
        IValidator<TemplatePurchaseRequest> purchaseValidator,
        ILogger<EconomyController> logger)
    {
        _economy = economy;
        _templates = templates;
        _workspaces = workspaces;
        _currentUser = currentUser;
        _onboardingValidator = onboardingValidator;
        _purchaseValidator = purchaseValidator;
        _logger = logger;
    }

    // ── Publisher (org owner) ────────────────────────────────────────────────

    [HttpGet("publisher")]
    [RequireOrgOwner]
    public async Task<ActionResult<PublisherStatus>> GetPublisher(CancellationToken ct) =>
        Ok(await _economy.GetPublisherStatusAsync(RequireOrg(), ct));

    [HttpPost("publisher/onboarding")]
    [RequireOrgOwner]
    public async Task<ActionResult<RedirectUrlResponse>> StartOnboarding([FromBody] PublisherOnboardingRequest request, CancellationToken ct)
    {
        await _onboardingValidator.ValidateAndThrowAsync(request, ct);
        _logger.LogInformation("EconomyController.StartOnboarding org={OrgId}", _currentUser.OrgId);
        return Ok(new RedirectUrlResponse(await _economy.StartOnboardingAsync(RequireOrg(), request.ReturnUrl, request.RefreshUrl, ct)));
    }

    [HttpPost("publisher/dashboard-link")]
    [RequireOrgOwner]
    public async Task<ActionResult<RedirectUrlResponse>> DashboardLink(CancellationToken ct) =>
        Ok(new RedirectUrlResponse(await _economy.CreateDashboardLinkAsync(RequireOrg(), ct)));

    [HttpGet("publisher/earnings")]
    [RequireOrgOwner]
    public async Task<ActionResult<PublisherEarningsSummary>> Earnings(CancellationToken ct) =>
        Ok(await _economy.GetEarningsAsync(RequireOrg(), ct));

    [HttpPut("publisher/listings/{templateId:guid}/pricing")]
    [RequireOrgOwner]
    public async Task<ActionResult<PublisherListing>> SetPricing(Guid templateId, [FromBody] TemplatePricingRequest request, CancellationToken ct) =>
        Ok(await _economy.SetPricingAsync(RequireOrg(), templateId, request, ct));

    // ── Buying (workspace) ───────────────────────────────────────────────────

    [HttpGet("workspaces/{workspaceId:guid}/templates/{templateId:guid}/access")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<TemplateAccess>> Access(Guid workspaceId, Guid templateId, CancellationToken ct)
    {
        var template = await _templates.GetByIdAsync(templateId, ct) ?? throw EconomyException.TemplateNotFound(templateId);
        return Ok(await _economy.GetAccessAsync(workspaceId, await WorkspaceOrgAsync(workspaceId, ct), template, ct));
    }

    /// <summary>Starts Checkout for a paid workflow. Spending the organisation's money needs the org owner.</summary>
    [HttpPost("workspaces/{workspaceId:guid}/templates/{templateId:guid}/purchase")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    [RequireOrgOwner]
    public async Task<ActionResult<RedirectUrlResponse>> Purchase(
        Guid workspaceId, Guid templateId, [FromBody] TemplatePurchaseRequest request, CancellationToken ct)
    {
        await _purchaseValidator.ValidateAndThrowAsync(request, ct);
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("A signed-in user is required to buy a workflow.");
        var url = await _economy.StartPurchaseAsync(workspaceId, await WorkspaceOrgAsync(workspaceId, ct), userId, templateId,
            request.SuccessUrl, request.CancelUrl, ct);
        return Ok(new RedirectUrlResponse(url));
    }

    [HttpGet("workspaces/{workspaceId:guid}/purchases")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<IReadOnlyList<TemplatePurchaseItem>>> Purchases(Guid workspaceId, CancellationToken ct) =>
        Ok(await _economy.ListPurchasesAsync(workspaceId, ct));

    [HttpPost("workspaces/{workspaceId:guid}/purchases/{purchaseId:guid}/cancel")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [RequireOrgOwner]
    public async Task<ActionResult<TemplatePurchaseItem>> Cancel(Guid workspaceId, Guid purchaseId, CancellationToken ct) =>
        Ok(await _economy.CancelPurchaseAsync(workspaceId, purchaseId, ct));

    private async Task<Guid> WorkspaceOrgAsync(Guid workspaceId, CancellationToken ct) =>
        (await _workspaces.GetByIdAsync(workspaceId, ct))?.OrgId
        ?? throw new EconomyException("WORKSPACE_NOT_FOUND", $"Workspace '{workspaceId}' was not found. Pick a workspace from the switcher.", 404);

    private Guid RequireOrg() =>
        _currentUser.OrgId ?? throw new EconomyException(
            "AUTH_REQUIRED", "Sign in to your organisation to manage publishing. Your session has no organisation context.", 401);
}

public sealed record PublisherOnboardingRequest(string ReturnUrl, string RefreshUrl);

public sealed record TemplatePurchaseRequest(string SuccessUrl, string CancelUrl);

public sealed record RedirectUrlResponse(string Url);
