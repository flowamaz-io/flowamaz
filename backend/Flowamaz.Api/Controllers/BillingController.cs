using Flowamaz.Api.Authorization;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Billing + Stripe (prompt 07-01): public pricing, the current org subscription, Checkout/Portal
/// session creation (org owner only), the Stripe webhook receiver, and current-period usage.
/// Stripe customer/secret ids are never returned to the frontend.
/// </summary>
[ApiController]
[Route("api/v1/billing")]
public sealed class BillingController : ControllerBase
{
    private readonly IStripeService _stripe;
    private readonly IPlanRepository _plans;
    private readonly IOrganisationRepository _organisations;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IUsageAggregateRepository _usage;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<CheckoutRequest> _checkoutValidator;
    private readonly IValidator<PortalRequest> _portalValidator;
    private readonly ILogger<BillingController> _logger;

    public BillingController(
        IStripeService stripe,
        IPlanRepository plans,
        IOrganisationRepository organisations,
        ISubscriptionRepository subscriptions,
        IUsageAggregateRepository usage,
        ICurrentUserService currentUser,
        IValidator<CheckoutRequest> checkoutValidator,
        IValidator<PortalRequest> portalValidator,
        ILogger<BillingController> logger)
    {
        _stripe = stripe;
        _plans = plans;
        _organisations = organisations;
        _subscriptions = subscriptions;
        _usage = usage;
        _currentUser = currentUser;
        _checkoutValidator = checkoutValidator;
        _portalValidator = portalValidator;
        _logger = logger;
    }

    /// <summary>Public list of purchasable plans for the pricing page.</summary>
    [HttpGet("plans")]
    [AllowAnonymous]
    // Plan definitions change only on a Stripe sync — cache the response for a day.
    [ResponseCache(Duration = 86400)]
    public async Task<ActionResult<IReadOnlyList<PlanResponse>>> GetPlans(CancellationToken cancellationToken)
    {
        _logger.LogInformation("BillingController.GetPlans enter");
        var slugs = new[] { "community", "starter", "pro", "enterprise" };
        var plans = new List<PlanResponse>();
        foreach (var slug in slugs)
        {
            var plan = await _plans.GetBySlugAsync(slug, cancellationToken);
            if (plan is null) continue;
            plans.Add(new PlanResponse(
                plan.Id, plan.Name, plan.Slug, plan.PriceMonthlyUsd, plan.PriceAnnualUsd,
                plan.Limits.MaxWorkflowDefinitions, plan.Limits.MaxRunsPerMonth, plan.Limits.MaxMembersPerWorkspace,
                plan.Limits.MaxWorkspaces, FeatureList(plan.Features)));
        }

        _logger.LogInformation("BillingController.GetPlans exit count={Count}", plans.Count);
        return Ok(plans);
    }

    /// <summary>The current org's subscription summary. No Stripe ids are exposed.</summary>
    [HttpGet("current")]
    [Authorize]
    public async Task<ActionResult<CurrentSubscriptionResponse>> GetCurrent(CancellationToken cancellationToken)
    {
        var orgId = RequireOrg();
        _logger.LogInformation("BillingController.GetCurrent enter org={OrgId}", orgId);

        var org = await _organisations.GetByIdAsync(orgId, cancellationToken)
            ?? throw BillingException.NoCustomer();
        var plan = await _plans.GetByIdAsync(org.PlanId, cancellationToken);
        var sub = await _subscriptions.GetByOrgIdAsync(orgId, cancellationToken);

        var status = sub?.Status.ToString().ToLowerInvariant() ?? org.Status.ToString().ToLowerInvariant();
        var trialEndsAt = sub?.TrialEndsAt ?? (org.Status == OrgStatus.Trial ? org.TrialEndsAt : (DateTime?)null);
        var daysRemaining = trialEndsAt is { } te ? Math.Max(0, (int)Math.Ceiling((te - DateTime.UtcNow).TotalDays)) : (int?)null;

        _logger.LogInformation("BillingController.GetCurrent exit org={OrgId} status={Status}", orgId, status);
        return Ok(new CurrentSubscriptionResponse(
            PlanId: org.PlanId,
            PlanName: plan?.Name ?? "Community",
            PlanSlug: plan?.Slug ?? "community",
            BillingCycle: (sub?.BillingCycle ?? BillingCycle.Monthly).ToString().ToLowerInvariant(),
            Status: status,
            TrialEndsAt: trialEndsAt,
            TrialDaysRemaining: daysRemaining,
            CurrentPeriodEnd: sub?.CurrentPeriodEnd,
            PaymentFailed: sub?.PaymentFailed ?? false,
            HasBillingAccount: !string.IsNullOrWhiteSpace(org.StripeCustomerId)));
    }

    /// <summary>Create a Stripe Checkout session for an upgrade. Returns the redirect URL.</summary>
    [HttpPost("checkout")]
    [RequireOrgOwner]
    public async Task<ActionResult<CheckoutSessionResponse>> Checkout(
        [FromBody] CheckoutRequest request, CancellationToken cancellationToken)
    {
        await _checkoutValidator.ValidateAndThrowAsync(request, cancellationToken);
        var orgId = RequireOrg();
        _logger.LogInformation("BillingController.Checkout enter org={OrgId} plan={PlanId}", orgId, request.PlanId);

        var url = await _stripe.CreateCheckoutSessionAsync(
            orgId, request.PlanId, request.Annual, request.SuccessUrl, request.CancelUrl, cancellationToken);

        _logger.LogInformation("BillingController.Checkout exit org={OrgId}", orgId);
        return Ok(new CheckoutSessionResponse(url));
    }

    /// <summary>Create a Stripe Customer Portal session. Returns the portal URL.</summary>
    [HttpPost("portal")]
    [RequireOrgOwner]
    public async Task<ActionResult<PortalSessionResponse>> Portal(
        [FromBody] PortalRequest request, CancellationToken cancellationToken)
    {
        await _portalValidator.ValidateAndThrowAsync(request, cancellationToken);
        var orgId = RequireOrg();
        _logger.LogInformation("BillingController.Portal enter org={OrgId}", orgId);

        var url = await _stripe.CreateCustomerPortalSessionAsync(orgId, request.ReturnUrl, cancellationToken);

        _logger.LogInformation("BillingController.Portal exit org={OrgId}", orgId);
        return Ok(new PortalSessionResponse(url));
    }

    /// <summary>Stripe webhook receiver. Signature is verified in the service; invalid → 400.</summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        _logger.LogInformation("BillingController.Webhook enter");

        Request.EnableBuffering();
        string payload;
        using (var reader = new StreamReader(Request.Body, leaveOpen: true))
        {
            payload = await reader.ReadToEndAsync(cancellationToken);
        }
        Request.Body.Position = 0;

        var signature = Request.Headers.TryGetValue("Stripe-Signature", out var values) ? values.ToString() : string.Empty;

        // BillingException (incl. invalid signature → 400) is mapped by GlobalExceptionMiddleware.
        await _stripe.HandleWebhookAsync(payload, signature, cancellationToken);

        _logger.LogInformation("BillingController.Webhook exit");
        return Ok(new { received = true });
    }

    /// <summary>Current-period usage for the org against its plan limits.</summary>
    [HttpGet("usage")]
    [RequireOrgOwner]
    public async Task<ActionResult<BillingUsageResponse>> Usage(CancellationToken cancellationToken)
    {
        var orgId = RequireOrg();
        _logger.LogInformation("BillingController.Usage enter org={OrgId}", orgId);

        var org = await _organisations.GetByIdAsync(orgId, cancellationToken)
            ?? throw BillingException.NoCustomer();
        var plan = await _plans.GetByIdAsync(org.PlanId, cancellationToken);
        var period = DateTime.UtcNow.ToString("yyyy-MM");
        var usage = await _usage.GetForPeriodAsync(orgId, period, cancellationToken);

        _logger.LogInformation("BillingController.Usage exit org={OrgId}", orgId);
        return Ok(new BillingUsageResponse(
            Period: period,
            RunsUsed: usage?.RunsCount ?? 0,
            RunsLimit: plan?.Limits.MaxRunsPerMonth ?? 0,
            AiCallsUsed: usage?.AiCallsCount ?? 0,
            AiCostUsd: usage?.AiCostUsd ?? 0m,
            MembersUsed: usage?.MemberPeak ?? 0,
            MembersLimit: plan?.Limits.MaxMembersPerWorkspace ?? 0,
            WorkflowsLimit: plan?.Limits.MaxWorkflowDefinitions ?? 0));
    }

    private Guid RequireOrg() =>
        _currentUser.OrgId ?? throw new BillingException(
            "AUTH_REQUIRED",
            "Sign in to your organisation to view billing. Your session has no organisation context.",
            httpStatusCode: 401);

    private static IReadOnlyList<string> FeatureList(Core.Models.PlanFeatures f)
    {
        var list = new List<string>();
        if (f.ApiAccess) list.Add("API access");
        if (f.CustomConnectors) list.Add("Custom connectors");
        if (f.AuditLog) list.Add("Audit log");
        if (f.ProcessIntelligence) list.Add("Process intelligence");
        if (f.RoiAnalytics) list.Add("ROI analytics");
        if (f.Sso) list.Add("SSO (SAML/OIDC)");
        if (f.Scim) list.Add("SCIM provisioning");
        if (f.Byom) list.Add("Bring your own model");
        if (f.DataResidency) list.Add("Data residency");
        return list;
    }
}

public sealed record PlanResponse(
    Guid Id,
    string Name,
    string Slug,
    decimal PriceMonthlyUsd,
    decimal PriceAnnualUsd,
    int MaxWorkflows,
    long MaxRunsMonth,
    int MaxMembers,
    int MaxWorkspaces,
    IReadOnlyList<string> Features);

public sealed record CurrentSubscriptionResponse(
    Guid PlanId,
    string PlanName,
    string PlanSlug,
    string BillingCycle,
    string Status,
    DateTime? TrialEndsAt,
    int? TrialDaysRemaining,
    DateTime? CurrentPeriodEnd,
    bool PaymentFailed,
    bool HasBillingAccount);

public sealed record CheckoutRequest(Guid PlanId, bool Annual, string SuccessUrl, string CancelUrl);

public sealed record CheckoutSessionResponse(string Url);

public sealed record PortalRequest(string ReturnUrl);

public sealed record PortalSessionResponse(string Url);

public sealed record BillingUsageResponse(
    string Period,
    long RunsUsed,
    long RunsLimit,
    long AiCallsUsed,
    decimal AiCostUsd,
    int MembersUsed,
    int MembersLimit,
    int WorkflowsLimit);
