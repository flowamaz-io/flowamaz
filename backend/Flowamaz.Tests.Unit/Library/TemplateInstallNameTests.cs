using FluentAssertions;
using Flowamaz.Application.Library.Services;

namespace Flowamaz.Tests.Unit.Library;

/// <summary>Run 5: an installed template kept the template's name in its YAML.</summary>
public class TemplateInstallNameTests
{
    [Fact]
    public void Block_style_name_is_replaced_and_comments_survive()
    {
        const string yaml = "apiVersion: flowamaz/v1\nkind: Workflow\n# Official template\nmetadata:\n  id: purchase-approval\n  name: \"Purchase Approval\"  # shown in lists\n  version: \"1.0.0\"\nspec: {}\n";
        var result = TemplateService.WithMetadataName(yaml, "Q4 Purchase Approval");
        result.Should().Contain("  name: \"Q4 Purchase Approval\"  # shown in lists")
            .And.Contain("# Official template").And.Contain("id: purchase-approval");
    }

    [Fact]
    public void Flow_style_and_plain_names_are_replaced()
    {
        TemplateService.WithMetadataName("metadata: { id: a, name: Purchase Approval }\nspec: {}\n", "Q4 \"Big\" buys")
            .Should().Contain("name: \"Q4 \\\"Big\\\" buys\" }");
    }

    [Fact]
    public void Yaml_without_metadata_name_is_unchanged()
    {
        const string yaml = "workflow: { id: legacy }\nnodes: []\n";
        TemplateService.WithMetadataName(yaml, "X").Should().BeSameAs(yaml);
    }
}
