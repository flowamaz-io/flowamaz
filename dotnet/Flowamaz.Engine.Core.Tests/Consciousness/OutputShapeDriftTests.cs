using System.Text.Json;
using FluentAssertions;
using Flowamaz.Core.Workflow.Consciousness;

namespace Flowamaz.Tests.Unit.Consciousness;

public class OutputShapeDriftTests
{
    private static List<JsonElement> Outputs(int n, string json) =>
        Enumerable.Range(0, n).Select(_ => JsonDocument.Parse(json).RootElement.Clone()).ToList();

    private static readonly Dictionary<string, List<string>> NoRefs = [];

    [Fact]
    public void A_field_that_disappeared_is_reported_and_flagged_when_the_workflow_reads_it()
    {
        var baseline = Outputs(8, """{"status":"ok","customer":{"id":7,"tier":"gold"}}""");
        var recent = Outputs(4, """{"status":"ok","customer":{"id":7}}""");
        var refs = OutputShapeDrift.ReferencedPaths("condition: \"{{ crm.output.customer.tier }} == 'gold'\"", "crm");

        var drift = OutputShapeDrift.Compare(baseline, recent, refs);

        drift.Should().ContainSingle();
        drift[0].Path.Should().Be("customer.tier");
        drift[0].Kind.Should().Be(DriftKind.Removed);
        drift[0].UsedBy.Should().ContainSingle().Which.Should().Be("{{ crm.output.customer.tier }}");
    }

    [Fact]
    public void A_number_now_sent_as_text_is_a_type_change()
    {
        var drift = OutputShapeDrift.Compare(Outputs(6, """{"total":120}"""), Outputs(3, """{"total":"120"}"""), NoRefs);

        drift.Should().ContainSingle(d => d.Path == "total" && d.Kind == DriftKind.TypeChanged
            && d.BaselineType == "number" && d.RecentType == "string");
    }

    [Fact]
    public void Only_the_outermost_missing_field_is_reported()
    {
        var drift = OutputShapeDrift.Compare(Outputs(6, """{"customer":{"id":1,"name":"a"}}"""), Outputs(3, """{"other":1}"""), NoRefs);
        drift.Should().ContainSingle().Which.Path.Should().Be("customer");
    }

    [Fact]
    public void Optional_fields_new_fields_and_thin_data_are_not_drift()
    {
        // "note" was there only sometimes before; "extra" is new. Neither is drift.
        var baseline = Outputs(6, """{"id":1,"note":"x"}""").Concat(Outputs(4, """{"id":1}""")).ToList();
        var recent = Outputs(3, """{"id":1,"extra":true}""");
        OutputShapeDrift.Compare(baseline, recent, NoRefs).Should().BeEmpty();

        OutputShapeDrift.Compare(Outputs(4, """{"a":1}"""), Outputs(3, "{}"), NoRefs).Should().BeEmpty("too few earlier runs");
        OutputShapeDrift.Compare(Outputs(6, """{"a":1}"""), Outputs(2, "{}"), NoRefs).Should().BeEmpty("too few recent runs");
    }

    [Fact]
    public void Arrays_and_index_references_are_matched_by_item_path()
    {
        var baseline = Outputs(6, """{"rows":[{"id":1},{"id":2}]}""");
        var recent = Outputs(3, """{"rows":[{"key":1}]}""");
        var refs = OutputShapeDrift.ReferencedPaths("text: \"first is {{ db.output.rows.0.id }}\"", "db");

        var drift = OutputShapeDrift.Compare(baseline, recent, refs);

        drift.Should().ContainSingle(d => d.Path == "rows[].id" && d.UsedBy.Count == 1);
    }

    [Fact]
    public void Nulls_count_as_missing()
    {
        OutputShapeDrift.Shape(JsonDocument.Parse("""{"a":null,"b":1}""").RootElement).Keys.Should().BeEquivalentTo(["b"]);
    }
}
