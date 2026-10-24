using System.Text.RegularExpressions;
using FluentAssertions;
using Flowamaz.Application.Workflow.Creation;
using Flowamaz.Application.Workflow.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Flowamaz.Tests.Unit.Workflow;

/// <summary>
/// Run 3 of the Chrome E2E: plain-English and conversation generation failed with a live key because the
/// prompt taught a schema the validator rejects, the output budget cut YAML off, and the self-correction
/// never showed the model its own YAML. These tests pin the prompt to the real validator.
/// </summary>
public class NlYamlPromptTests(ITestOutputHelper output)
{
    private static readonly WorkflowValidator Validator = new(NullLogger<WorkflowValidator>.Instance);

    public static IEnumerable<object[]> PromptExamples()
    {
        var prompt = NlYamlGenerationService.SystemPrompt;
        foreach (Match m in Regex.Matches(prompt, @"EXAMPLE \d+[^\n]*\n(apiVersion:.*?)(?=\n\s*\n)", RegexOptions.Singleline))
            yield return [m.Groups[1].Value];
    }

    [Fact]
    public void Prompt_has_two_examples() => PromptExamples().Should().HaveCount(2);

    [Theory]
    [MemberData(nameof(PromptExamples))]
    public async Task Every_example_in_the_prompt_validates_without_errors(string yaml)
    {
        var result = await Validator.ValidateAsync(yaml, Guid.NewGuid());
        foreach (var e in result.Errors) output.WriteLine($"{e.Code} {e.NodeId} {e.Message}");
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().NotContain(w => w.Code == "BPR-011", "examples must not teach single-exit decisions");
        result.Warnings.Should().NotContain(w => w.Code == "BPR-012", "examples must not escalate to nobody");
    }

    [Fact]
    public void Prompt_never_teaches_dollar_variables_or_invalid_trigger_types()
    {
        var prompt = NlYamlGenerationService.SystemPrompt;
        prompt.Should().NotContain("${").And.NotContain("type: event");
    }

    [Fact]
    public void ExtractYaml_takes_the_fenced_block_even_after_a_sentence()
    {
        var text = "Here is your workflow:\n```yaml\napiVersion: flowamaz/v1\nkind: Workflow\n```\nLet me know!";
        NlYamlGenerationService.ExtractYaml(text).Should().Be("apiVersion: flowamaz/v1\nkind: Workflow");
    }

    [Fact]
    public void ExtractYaml_starts_at_apiVersion_when_there_is_no_fence()
    {
        NlYamlGenerationService.ExtractYaml("Sure.\napiVersion: flowamaz/v1\nkind: Workflow")
            .Should().Be("apiVersion: flowamaz/v1\nkind: Workflow");
    }
}
