using FluentAssertions;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Entities.Ai;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Models;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flowamaz.Tests.Unit.Ai;

/// <summary>
/// Covers the workspace AI-config read view (resolved per-function model + level + budget) and the
/// update validation gates: org provider allowlist, model-in-catalogue, provider match, and the
/// per-function capability gate. The org allowlist is sourced from OrgAiConfig.ProvidersEnabled so
/// these tests never depend on plan seed data.
/// </summary>
public class WorkspaceAiConfigServiceTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _orgId = Guid.NewGuid();

    private FlowAmazDbContext NewDb(string name) =>
        new(new DbContextOptionsBuilder<FlowAmazDbContext>()
            .UseInMemoryDatabase($"wsaiconfig-{name}-{Guid.NewGuid():N}")
            .Options);

    private WorkspaceAiConfigService NewService(FlowAmazDbContext db) =>
        new(db, NullLogger<WorkspaceAiConfigService>.Instance);

    private static ModelCatalogue Haiku() => new()
    {
        ModelId = "claude-haiku-4-5", Provider = AiProviders.Anthropic, DisplayName = "Haiku",
        HasVision = false, MaxContextTokens = 200_000, SupportsJsonMode = true, SupportsStreaming = true, IsEnabled = true,
    };

    private static ModelCatalogue Gemini() => new()
    {
        ModelId = "gemini-2-0-flash", Provider = AiProviders.Google, DisplayName = "Gemini",
        HasVision = true, MaxContextTokens = 1_000_000, SupportsJsonMode = true, SupportsStreaming = true, IsEnabled = true,
    };

    private void SeedWorkspaceAndOrg(FlowAmazDbContext db, params string[] allowedProviders)
    {
        db.Workspaces.Add(new Workspace { Id = _workspaceId, OrgId = _orgId, Name = "WS", Slug = "ws" });
        db.OrgAiConfigs.Add(new OrgAiConfig { OrgId = _orgId, ProvidersEnabled = [.. allowedProviders] });
    }

    // ── GetAsync ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_returns_allowed_providers_budget_and_resolved_functions()
    {
        await using var db = NewDb(nameof(GetAsync_returns_allowed_providers_budget_and_resolved_functions));
        SeedWorkspaceAndOrg(db, AiProviders.Anthropic, AiProviders.Google);
        db.PlatformAiConfigs.Add(new PlatformAiConfig
        {
            FunctionId = AiFunctionIds.Copilot, Provider = AiProviders.Anthropic,
            ModelId = "claude-haiku-4-5", KeySource = Core.Enums.AiKeySource.Platform, IsEnabled = true,
        });
        db.WorkspaceAiConfigs.Add(new WorkspaceAiConfig
        {
            WorkspaceId = _workspaceId,
            FunctionOverridesJson = """{"nl-yaml":{"provider":"google","modelId":"gemini-2-0-flash","keySource":"Byok"}}""",
        });
        db.WorkspaceAiBudgets.Add(new WorkspaceAiBudget
        {
            WorkspaceId = _workspaceId, MonthlyTokenLimit = 1_000_000, TokensUsedThisMonth = 250_000,
            BudgetResetDate = DateTime.UtcNow.AddDays(10), IsHardCapped = true,
        });
        await db.SaveChangesAsync();

        var view = await NewService(db).GetAsync(_workspaceId);

        view.AllowedProviders.Should().Contain(AiProviders.Anthropic).And.Contain(AiProviders.Google);
        view.Budget.Should().NotBeNull();
        view.Budget!.MonthlyTokenLimit.Should().Be(1_000_000);

        view.Functions.Should().Contain(f => f.FunctionId == AiFunctionIds.Copilot && f.ResolvedFrom == "platform");
        view.Functions.Should().Contain(f => f.FunctionId == AiFunctionIds.NlYaml && f.ResolvedFrom == "workspace" && f.ModelId == "gemini-2-0-flash");
    }

    [Fact]
    public async Task GetAsync_unknown_workspace_throws()
    {
        await using var db = NewDb(nameof(GetAsync_unknown_workspace_throws));
        await db.SaveChangesAsync();

        await NewService(db).Invoking(s => s.GetAsync(Guid.NewGuid()))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    // ── UpdateAsync validation gates ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_provider_not_in_org_allowlist_throws()
    {
        await using var db = NewDb(nameof(UpdateAsync_provider_not_in_org_allowlist_throws));
        SeedWorkspaceAndOrg(db, AiProviders.Anthropic); // google not allowed
        db.ModelCatalogue.Add(Gemini());
        await db.SaveChangesAsync();

        var overrides = new[] { new FunctionOverrideInput(AiFunctionIds.Copilot, AiProviders.Google, "gemini-2-0-flash", "Byok") };
        var ex = await NewService(db).Invoking(s => s.UpdateAsync(_workspaceId, overrides))
            .Should().ThrowAsync<ConfigViolationException>();
        ex.Which.ErrorCode.Should().Be("AI_PROVIDER_NOT_ALLOWED");
    }

    [Fact]
    public async Task UpdateAsync_unknown_model_throws()
    {
        await using var db = NewDb(nameof(UpdateAsync_unknown_model_throws));
        SeedWorkspaceAndOrg(db, AiProviders.Anthropic);
        await db.SaveChangesAsync();

        var overrides = new[] { new FunctionOverrideInput(AiFunctionIds.Copilot, AiProviders.Anthropic, "ghost-model", "Platform") };
        var ex = await NewService(db).Invoking(s => s.UpdateAsync(_workspaceId, overrides))
            .Should().ThrowAsync<ConfigViolationException>();
        ex.Which.ErrorCode.Should().Be("AI_MODEL_UNKNOWN");
    }

    [Fact]
    public async Task UpdateAsync_model_provider_mismatch_throws()
    {
        await using var db = NewDb(nameof(UpdateAsync_model_provider_mismatch_throws));
        SeedWorkspaceAndOrg(db, AiProviders.Anthropic, AiProviders.Google);
        db.ModelCatalogue.Add(Gemini()); // gemini is google
        await db.SaveChangesAsync();

        // Claims anthropic but the catalogued model belongs to google.
        var overrides = new[] { new FunctionOverrideInput(AiFunctionIds.Copilot, AiProviders.Anthropic, "gemini-2-0-flash", "Byok") };
        var ex = await NewService(db).Invoking(s => s.UpdateAsync(_workspaceId, overrides))
            .Should().ThrowAsync<ConfigViolationException>();
        ex.Which.ErrorCode.Should().Be("AI_MODEL_PROVIDER_MISMATCH");
    }

    [Fact]
    public async Task UpdateAsync_F3_visual_input_without_vision_model_throws_capability_gate()
    {
        await using var db = NewDb(nameof(UpdateAsync_F3_visual_input_without_vision_model_throws_capability_gate));
        SeedWorkspaceAndOrg(db, AiProviders.Anthropic);
        db.ModelCatalogue.Add(Haiku()); // no vision
        await db.SaveChangesAsync();

        var overrides = new[] { new FunctionOverrideInput(AiFunctionIds.VisualInput, AiProviders.Anthropic, "claude-haiku-4-5", "Platform") };
        var ex = await NewService(db).Invoking(s => s.UpdateAsync(_workspaceId, overrides))
            .Should().ThrowAsync<ConfigViolationException>();
        ex.Which.ErrorCode.Should().Be("AI_CAPABILITY_MISMATCH");
    }

    [Fact]
    public async Task UpdateAsync_valid_override_persists_merged_json()
    {
        await using var db = NewDb(nameof(UpdateAsync_valid_override_persists_merged_json));
        SeedWorkspaceAndOrg(db, AiProviders.Anthropic);
        db.ModelCatalogue.Add(Haiku());
        await db.SaveChangesAsync();

        var overrides = new[] { new FunctionOverrideInput(AiFunctionIds.Copilot, AiProviders.Anthropic, "claude-haiku-4-5", "Platform") };
        await NewService(db).UpdateAsync(_workspaceId, overrides);

        var row = await db.WorkspaceAiConfigs.FirstOrDefaultAsync(c => c.WorkspaceId == _workspaceId);
        row.Should().NotBeNull();
        row!.FunctionOverridesJson.Should().Contain(AiFunctionIds.Copilot).And.Contain("claude-haiku-4-5");
    }
}
