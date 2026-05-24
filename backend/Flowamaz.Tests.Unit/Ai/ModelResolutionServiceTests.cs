using FluentAssertions;
using Flowamaz.Core.Configuration;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Entities.Ai;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Flowamaz.Tests.Unit.Ai;

public class ModelResolutionServiceTests
{
    private static FlowAmazDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<FlowAmazDbContext>()
            .UseInMemoryDatabase($"resolve-{name}-{Guid.NewGuid():N}")
            .Options;
        return new FlowAmazDbContext(options);
    }

    private static ModelResolutionService NewService(FlowAmazDbContext db) =>
        new(db,
            Options.Create(new AiOptions
            {
                AnthropicPlatformKey = "anthropic-key-test",
                GooglePlatformKey = "google-key-test",
            }),
            NullLogger<ModelResolutionService>.Instance);

    private static ModelCatalogue HaikuModel() => new()
    {
        ModelId = "claude-haiku-4-5",
        Provider = AiProviders.Anthropic,
        DisplayName = "Claude Haiku 4.5",
        HasVision = false,
        MaxContextTokens = 200_000,
        SupportsJsonMode = true,
        SupportsStreaming = true,
        IsEnabled = true,
    };

    private static ModelCatalogue SonnetModel() => new()
    {
        ModelId = "claude-sonnet-4-6",
        Provider = AiProviders.Anthropic,
        DisplayName = "Claude Sonnet 4.6",
        HasVision = true,
        MaxContextTokens = 200_000,
        SupportsJsonMode = true,
        SupportsStreaming = true,
        IsEnabled = true,
    };

    private static ModelCatalogue Moonshot32k() => new()
    {
        ModelId = "moonshot-v1-32k",
        Provider = AiProviders.Kimi,
        DisplayName = "Moonshot v1 32k",
        HasVision = false,
        MaxContextTokens = 32_000,
        SupportsJsonMode = true,
        SupportsStreaming = false,
        IsEnabled = true,
    };

    [Fact]
    public async Task F3_visual_input_with_no_vision_model_throws_capability_mismatch()
    {
        // Arrange — platform forced to use a non-vision model for F3 (the bad config).
        await using var db = NewDb(nameof(F3_visual_input_with_no_vision_model_throws_capability_mismatch));
        db.ModelCatalogue.Add(HaikuModel());
        db.PlatformAiConfigs.Add(new PlatformAiConfig
        {
            FunctionId = AiFunctionIds.VisualInput,
            Provider = AiProviders.Anthropic,
            ModelId = "claude-haiku-4-5", // Haiku has no vision — should be rejected
            KeySource = AiKeySource.Platform,
            IsEnabled = true,
        });
        await db.SaveChangesAsync();
        var service = NewService(db);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<ConfigViolationException>(() =>
            service.ResolveModelConfigAsync(AiFunctionIds.VisualInput, Guid.NewGuid()));
        ex.ErrorCode.Should().Be("AI_CAPABILITY_MISMATCH");
        ex.Message.Should().Contain("vision");
    }

    [Fact]
    public async Task F7_doc_parse_with_small_context_model_throws_capability_mismatch()
    {
        await using var db = NewDb(nameof(F7_doc_parse_with_small_context_model_throws_capability_mismatch));
        db.ModelCatalogue.Add(Moonshot32k());
        db.PlatformAiConfigs.Add(new PlatformAiConfig
        {
            FunctionId = AiFunctionIds.DocParse,
            Provider = AiProviders.Kimi,
            ModelId = "moonshot-v1-32k", // 32k < 100k threshold
            KeySource = AiKeySource.Platform,
            IsEnabled = true,
        });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var ex = await Assert.ThrowsAsync<ConfigViolationException>(() =>
            service.ResolveModelConfigAsync(AiFunctionIds.DocParse, Guid.NewGuid()));
        ex.ErrorCode.Should().Be("AI_CAPABILITY_MISMATCH");
        ex.Message.Should().Contain("100,000");
    }

    [Fact]
    public async Task Resolves_from_platform_when_no_workspace_or_org_override()
    {
        await using var db = NewDb(nameof(Resolves_from_platform_when_no_workspace_or_org_override));
        db.ModelCatalogue.Add(HaikuModel());
        db.PlatformAiConfigs.Add(new PlatformAiConfig
        {
            FunctionId = AiFunctionIds.Copilot,
            Provider = AiProviders.Anthropic,
            ModelId = "claude-haiku-4-5",
            KeySource = AiKeySource.Platform,
            IsEnabled = true,
        });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var result = await service.ResolveModelConfigAsync(AiFunctionIds.Copilot, Guid.NewGuid());

        result.ModelId.Should().Be("claude-haiku-4-5");
        result.Provider.Should().Be(AiProviders.Anthropic);
        result.ApiKeySource.Should().Be(AiKeySource.Platform);
        result.ApiKey.Should().Be("anthropic-key-test"); // resolved from AiOptions
    }

    [Fact]
    public async Task Workspace_override_wins_over_platform()
    {
        var workspaceId = Guid.NewGuid();
        await using var db = NewDb(nameof(Workspace_override_wins_over_platform));
        db.ModelCatalogue.AddRange(HaikuModel(), SonnetModel());
        db.PlatformAiConfigs.Add(new PlatformAiConfig
        {
            FunctionId = AiFunctionIds.Copilot,
            Provider = AiProviders.Anthropic,
            ModelId = "claude-haiku-4-5",
            KeySource = AiKeySource.Platform,
            IsEnabled = true,
        });
        db.WorkspaceAiConfigs.Add(new WorkspaceAiConfig
        {
            WorkspaceId = workspaceId,
            FunctionOverridesJson = """
                {"copilot":{"provider":"anthropic","modelId":"claude-sonnet-4-6","keySource":"Byok"}}
                """,
        });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var result = await service.ResolveModelConfigAsync(AiFunctionIds.Copilot, workspaceId);

        result.ModelId.Should().Be("claude-sonnet-4-6"); // workspace beat platform Haiku
        result.ApiKeySource.Should().Be(AiKeySource.Byok);
        result.ApiKey.Should().BeNull(); // BYOK key is fetched by caller from vault
    }

    [Fact]
    public async Task Disabled_model_throws_config_violation()
    {
        await using var db = NewDb(nameof(Disabled_model_throws_config_violation));
        var disabled = HaikuModel();
        disabled.IsEnabled = false;
        db.ModelCatalogue.Add(disabled);
        db.PlatformAiConfigs.Add(new PlatformAiConfig
        {
            FunctionId = AiFunctionIds.Copilot,
            Provider = AiProviders.Anthropic,
            ModelId = "claude-haiku-4-5",
            KeySource = AiKeySource.Platform,
            IsEnabled = true,
        });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var ex = await Assert.ThrowsAsync<ConfigViolationException>(() =>
            service.ResolveModelConfigAsync(AiFunctionIds.Copilot, Guid.NewGuid()));
        ex.ErrorCode.Should().Be("MODEL_DISABLED");
    }

    [Fact]
    public async Task Unknown_function_throws_invalid_operation()
    {
        await using var db = NewDb(nameof(Unknown_function_throws_invalid_operation));
        await db.SaveChangesAsync();
        var service = NewService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResolveModelConfigAsync("not-a-real-function", Guid.NewGuid()));
    }
}
