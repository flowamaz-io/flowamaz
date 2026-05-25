using Flowamaz.Core.Constants;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Models;

namespace Flowamaz.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotent seed data for the four subscription plans (FUNCTIONAL.md §13.2). Fixed Guids and a
/// fixed timestamp keep the migration INSERTs deterministic and re-runnable. Applied via HasData
/// in <see cref="Configurations.PlanConfiguration"/>. Annual price = 10× monthly (≈2 months free).
/// </summary>
internal static class PlatformSeedData
{
    private static readonly DateTime SeedTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static readonly Guid CommunityPlanId = new("a1000000-0000-0000-0000-000000000001");
    public static readonly Guid StarterPlanId = new("a1000000-0000-0000-0000-000000000002");
    public static readonly Guid ProPlanId = new("a1000000-0000-0000-0000-000000000003");
    public static readonly Guid EnterprisePlanId = new("a1000000-0000-0000-0000-000000000004");

    public static IReadOnlyList<Plan> Plans { get; } =
    [
        new Plan
        {
            Id = CommunityPlanId,
            Name = "Community",
            Slug = "community",
            PriceMonthlyUsd = 0m,
            PriceAnnualUsd = 0m,
            Limits = new PlanLimits
            {
                MaxWorkspaces = 1,
                MaxMembersPerWorkspace = 1,
                MaxWorkflowDefinitions = 5,
                MaxRunsPerMonth = 500,
                MaxAiCallsPerMonth = 0,
                MaxStorageGb = 1,
                RunRetentionDays = 7,
                ApiRateLimitPerMinute = 10,
                AllowedAiProviders = [AiProviders.Anthropic],
            },
            Features = new PlanFeatures(),
            IsActive = true,
            IsPublic = true,
            CreatedAt = SeedTimestamp,
            UpdatedAt = SeedTimestamp,
        },
        new Plan
        {
            Id = StarterPlanId,
            Name = "Starter",
            Slug = "starter",
            PriceMonthlyUsd = 49m,
            PriceAnnualUsd = 490m,
            Limits = new PlanLimits
            {
                MaxWorkspaces = 3,
                MaxMembersPerWorkspace = 10,
                MaxWorkflowDefinitions = 20,
                MaxRunsPerMonth = 5000,
                MaxAiCallsPerMonth = 0,
                MaxStorageGb = 10,
                RunRetentionDays = 90,
                ApiRateLimitPerMinute = 60,
                AllowedAiProviders = [AiProviders.Anthropic, AiProviders.AzureOpenAi, AiProviders.Google],
            },
            Features = new PlanFeatures
            {
                ApiAccess = true,
                CustomConnectors = true,
            },
            IsActive = true,
            IsPublic = true,
            CreatedAt = SeedTimestamp,
            UpdatedAt = SeedTimestamp,
        },
        new Plan
        {
            Id = ProPlanId,
            Name = "Pro",
            Slug = "pro",
            PriceMonthlyUsd = 199m,
            PriceAnnualUsd = 1990m,
            Limits = new PlanLimits
            {
                MaxWorkspaces = 10,
                MaxMembersPerWorkspace = 50,
                MaxWorkflowDefinitions = 0,
                MaxRunsPerMonth = 50000,
                MaxAiCallsPerMonth = 0,
                MaxStorageGb = 50,
                RunRetentionDays = 365,
                ApiRateLimitPerMinute = 300,
                AllowedAiProviders =
                    [AiProviders.Anthropic, AiProviders.AzureOpenAi, AiProviders.Google, AiProviders.Kimi, AiProviders.Mistral],
            },
            Features = new PlanFeatures
            {
                ApiAccess = true,
                CustomConnectors = true,
                AuditLog = true,
                ProcessIntelligence = true,
                RoiAnalytics = true,
            },
            IsActive = true,
            IsPublic = true,
            CreatedAt = SeedTimestamp,
            UpdatedAt = SeedTimestamp,
        },
        new Plan
        {
            Id = EnterprisePlanId,
            Name = "Enterprise",
            Slug = "enterprise",
            PriceMonthlyUsd = 0m,
            PriceAnnualUsd = 0m,
            Limits = new PlanLimits
            {
                MaxWorkspaces = 0,
                MaxMembersPerWorkspace = 0,
                MaxWorkflowDefinitions = 0,
                MaxRunsPerMonth = 0,
                MaxAiCallsPerMonth = 0,
                MaxStorageGb = 0,
                RunRetentionDays = 0,
                ApiRateLimitPerMinute = 0,
                AllowedAiProviders =
                    [AiProviders.Anthropic, AiProviders.AzureOpenAi, AiProviders.Google, AiProviders.Kimi, AiProviders.Mistral, AiProviders.Byom],
            },
            Features = new PlanFeatures
            {
                Sso = true,
                Scim = true,
                CustomConnectors = true,
                AuditLog = true,
                ApiAccess = true,
                Byom = true,
                DataResidency = true,
                Cmek = true,
                ProcessIntelligence = true,
                RoiAnalytics = true,
            },
            IsActive = true,
            IsPublic = false,
            CreatedAt = SeedTimestamp,
            UpdatedAt = SeedTimestamp,
        },
    ];
}
