using FluentAssertions;
using Flowamaz.Api.Controllers;

namespace Flowamaz.Tests.Unit.Workflow.Creation;

/// <summary>Run 4: conversation and photo workflows were saved as "Untitled Workflow" although the AI named them.</summary>
public class GeneratedWorkflowNameTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Untitled Workflow")]
    [InlineData("visual workflow")]
    public void Placeholder_names_are_treated_as_no_name(string? name) =>
        WorkflowCreationController.IsPlaceholderName(name).Should().BeTrue();

    [Fact]
    public void A_real_name_is_kept() =>
        WorkflowCreationController.IsPlaceholderName("Laptop Request Approval").Should().BeFalse();

    [Fact]
    public void NameFromYaml_reads_metadata_name()
    {
        const string yaml = "apiVersion: flowamaz/v1\nkind: Workflow\nmetadata:\n  id: laptop\n  name: \"Laptop Request Approval Process\"\nspec: {}\n";
        WorkflowCreationController.NameFromYaml(yaml).Should().Be("Laptop Request Approval Process");
    }

    [Theory]
    [InlineData("apiVersion: flowamaz/v1\nmetadata:\n  name: Visual Workflow\n")]
    [InlineData("apiVersion: flowamaz/v1\nmetadata: {}\n")]
    [InlineData(": not [ yaml")]
    [InlineData("")]
    public void NameFromYaml_ignores_generic_missing_or_broken(string yaml) =>
        WorkflowCreationController.NameFromYaml(yaml).Should().BeNull();
}
