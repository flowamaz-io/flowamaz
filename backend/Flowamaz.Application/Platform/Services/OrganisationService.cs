using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Platform.Services;

/// <summary>
/// Provisions organisations. Registration is atomic (org + owner + trial subscription commit
/// together); the welcome email is sent after commit and never rolls registration back.
/// </summary>
public sealed class OrganisationService : IOrganisationService
{
    private const int TrialLengthDays = 14;
    private const int BcryptWorkFactor = 12;
    private const string WelcomeSubject = "Welcome to Flowamaz — your 14-day trial has started";
    private const string AppUrl = "https://app.flowamaz.io";
    private const string SupportEmail = "support@flowamaz.io";

    private readonly IPlanRepository _planRepository;
    private readonly IOrganisationRepository _organisationRepository;
    private readonly IOrgUserRepository _orgUserRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailService _emailService;
    private readonly ILogger<OrganisationService> _logger;

    public OrganisationService(
        IPlanRepository planRepository,
        IOrganisationRepository organisationRepository,
        IOrgUserRepository orgUserRepository,
        IUnitOfWork unitOfWork,
        IEmailService emailService,
        ILogger<OrganisationService> logger)
    {
        _planRepository = planRepository;
        _organisationRepository = organisationRepository;
        _orgUserRepository = orgUserRepository;
        _unitOfWork = unitOfWork;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<OrgRegistrationResult> RegisterOrganisationAsync(
        string name,
        string slug,
        string billingEmail,
        string ownerEmail,
        string ownerName,
        string ownerPassword,
        string planSlug,
        DataRegion dataRegion,
        CancellationToken cancellationToken = default)
    {
        var normalisedSlug = (slug ?? string.Empty).Trim().ToLowerInvariant();
        _logger.LogDebug(
            "OrganisationService.RegisterOrganisationAsync enter slug={Slug} ownerEmail={OwnerEmail} plan={PlanSlug} region={DataRegion}",
            normalisedSlug, ownerEmail, planSlug, dataRegion);

        try
        {
            var plan = await _planRepository.GetBySlugAsync(planSlug, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Plan '{planSlug}' was not found — plan seed data is missing or the slug is invalid.");

            if (await _organisationRepository.SlugExistsAsync(normalisedSlug, cancellationToken))
            {
                throw new SlugAlreadyExistsException(normalisedSlug);
            }

            var now = DateTime.UtcNow;
            var trialEndsAt = now.AddDays(TrialLengthDays);

            var organisation = new Organisation
            {
                Name = name,
                Slug = normalisedSlug,
                BillingEmail = billingEmail,
                PlanId = plan.Id,
                Status = OrgStatus.Trial,
                TrialEndsAt = trialEndsAt,
                DataRegion = dataRegion,
            };

            var owner = new OrgUser
            {
                OrgId = organisation.Id,
                Email = ownerEmail,
                Name = ownerName,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(ownerPassword, BcryptWorkFactor),
                IsOrgOwner = true,
                IsActive = true,
            };

            var subscription = new Subscription
            {
                OrgId = organisation.Id,
                PlanId = plan.Id,
                BillingCycle = BillingCycle.Monthly,
                CurrentPeriodStart = now,
                CurrentPeriodEnd = trialEndsAt,
                OvercapCapUsd = 0m,
                Status = SubscriptionStatus.Active,
            };

            await using (var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken))
            {
                try
                {
                    await _organisationRepository.AddAsync(organisation, cancellationToken);
                    await _orgUserRepository.AddAsync(owner, cancellationToken);
                    await _organisationRepository.AddSubscriptionAsync(subscription, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }

            await SendWelcomeEmailAsync(owner, cancellationToken);

            _logger.LogDebug(
                "OrganisationService.RegisterOrganisationAsync exit orgId={OrgId} ownerUserId={OwnerUserId} slug={Slug}",
                organisation.Id, owner.Id, normalisedSlug);

            return new OrgRegistrationResult(
                organisation.Id, organisation.Slug, owner.Id, owner.Email,
                plan.Slug, organisation.Status, organisation.TrialEndsAt);
        }
        catch (SlugAlreadyExistsException)
        {
            // Expected business outcome — surfaced to the caller without an error log.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "OrganisationService.RegisterOrganisationAsync error slug={Slug} ownerEmail={OwnerEmail}",
                normalisedSlug, ownerEmail);
            throw;
        }
    }

    public async Task<Organisation?> GetByIdAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OrganisationService.GetByIdAsync enter orgId={OrgId}", orgId);
        try
        {
            var org = await _organisationRepository.GetByIdAsync(orgId, cancellationToken);
            _logger.LogDebug("OrganisationService.GetByIdAsync exit orgId={OrgId} found={Found}", orgId, org is not null);
            return org;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrganisationService.GetByIdAsync error orgId={OrgId}", orgId);
            throw;
        }
    }

    public async Task<Organisation?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OrganisationService.GetBySlugAsync enter slug={Slug}", slug);
        try
        {
            var org = await _organisationRepository.GetBySlugAsync(slug, cancellationToken);
            _logger.LogDebug("OrganisationService.GetBySlugAsync exit slug={Slug} found={Found}", slug, org is not null);
            return org;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrganisationService.GetBySlugAsync error slug={Slug}", slug);
            throw;
        }
    }

    public async Task<PlanLimits> GetPlanLimitsAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OrganisationService.GetPlanLimitsAsync enter orgId={OrgId}", orgId);
        try
        {
            var org = await _organisationRepository.GetByIdAsync(orgId, cancellationToken)
                ?? throw new InvalidOperationException($"Organisation '{orgId}' was not found.");
            var plan = await _planRepository.GetByIdAsync(org.PlanId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Plan '{org.PlanId}' for organisation '{orgId}' was not found — data is inconsistent.");

            _logger.LogDebug("OrganisationService.GetPlanLimitsAsync exit orgId={OrgId} plan={PlanSlug}", orgId, plan.Slug);
            return plan.Limits;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrganisationService.GetPlanLimitsAsync error orgId={OrgId}", orgId);
            throw;
        }
    }

    private async Task SendWelcomeEmailAsync(OrgUser owner, CancellationToken cancellationToken)
    {
        _logger.LogDebug("OrganisationService.SendWelcomeEmailAsync enter ownerUserId={OwnerUserId}", owner.Id);
        try
        {
            var sent = await _emailService.SendAsync(
                owner.Email, WelcomeSubject, BuildWelcomeHtml(owner.Name), BuildWelcomeText(owner.Name), cancellationToken);

            if (sent)
            {
                _logger.LogInformation(
                    "OrganisationService.SendWelcomeEmailAsync exit ownerUserId={OwnerUserId} status=sent", owner.Id);
            }
            else
            {
                _logger.LogWarning(
                    "OrganisationService.SendWelcomeEmailAsync exit ownerUserId={OwnerUserId} status=failed — welcome email not delivered; registration is unaffected",
                    owner.Id);
            }
        }
        catch (Exception ex)
        {
            // A completed registration must never be undone by a notification failure (prompt 02).
            _logger.LogWarning(ex,
                "OrganisationService.SendWelcomeEmailAsync error ownerUserId={OwnerUserId} — welcome email failed; registration is unaffected",
                owner.Id);
        }
    }

    private static string BuildWelcomeHtml(string ownerName) =>
        $"""
        <p>Hi {ownerName},</p>
        <p>Welcome to Flowamaz — your 14-day trial has started.</p>
        <p>Open your workspace and create your first workflow at <a href="{AppUrl}">{AppUrl}</a>.</p>
        <p>Need a hand? Email us at <a href="mailto:{SupportEmail}">{SupportEmail}</a>.</p>
        <p>— The Flowamaz team</p>
        """;

    private static string BuildWelcomeText(string ownerName) =>
        $"""
        Hi {ownerName},

        Welcome to Flowamaz — your 14-day trial has started.

        Open your workspace and create your first workflow at {AppUrl}.

        Need a hand? Email us at {SupportEmail}.

        — The Flowamaz team
        """;
}
