using FluentAssertions;
using Flowamaz.Infrastructure.Services;

namespace Flowamaz.Tests.Unit.Ai;

public class SemanticCacheNormalisationTests
{
    [Theory]
    [InlineData("Add a timeout", "add a timeout")]
    [InlineData("  Add a timeout  ", "add a timeout")]
    [InlineData("ADD\t a\n timeout", "add a timeout")]
    [InlineData("Add   a    timeout", "add a timeout")]
    public void NormaliseCommand_collapses_case_and_whitespace(string input, string expected)
    {
        SemanticCacheService.NormaliseCommand(input).Should().Be(expected);
    }
}
