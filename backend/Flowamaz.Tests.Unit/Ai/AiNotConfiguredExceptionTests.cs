using FluentAssertions;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Models;
using Xunit;

namespace Flowamaz.Tests.Unit.Ai;

/// <summary>Generation must refuse to run on stub output when no provider key resolves.</summary>
public class AiNotConfiguredExceptionTests
{
    private static ModelConfig Config(string? key) =>
        new("claude-sonnet-4-6", "anthropic", AiKeySource.Platform, key, true, 200_000, true, true);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ThrowIfNoKey_without_a_key_throws_503_naming_the_feature_and_the_fix(string? key)
    {
        var act = () => AiNotConfiguredException.ThrowIfNoKey(Config(key), "Generating a workflow from plain English");

        var ex = act.Should().Throw<AiNotConfiguredException>().Which;
        ex.ErrorCode.Should().Be("AI_NOT_CONFIGURED");
        ex.HttpStatusCode.Should().Be(503);
        ex.Message.Should().StartWith("Generating a workflow from plain English needs an AI provider key")
            .And.Contain("Settings → Workspace → AI model configuration")
            .And.Contain("Draw on canvas");
    }

    [Fact]
    public void ThrowIfNoKey_with_a_key_does_nothing()
    {
        var act = () => AiNotConfiguredException.ThrowIfNoKey(Config("sk-test"), "Anything");
        act.Should().NotThrow();
    }
}
