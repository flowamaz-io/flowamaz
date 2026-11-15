using System.Text.Json;
using FluentAssertions;
using Flowamaz.Core.Workflow;

namespace Flowamaz.Tests.Unit.Workflow;

/// <summary>Human-gate forms (FUNCTIONAL.md §23.2): definition checks and the approver's values.</summary>
public sealed class GateFormsTests
{
    private static readonly GateFormField[] Purchase =
    [
        new("amount", "Approved amount", "number", Required: true, Min: 0, Max: 50000),
        new("cost_centre", "Cost centre", "text", Required: true),
        new("budget_line", "Budget line", "select", Options: ["Operating", "Capital"]),
        new("receipts", "Receipts checked", "checkbox", Required: true),
        new("start", "Start date", "date"),
        new("cover", "Cover", "email"),
    ];

    private static Dictionary<string, JsonElement> Values(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public void Read_TakesFieldsFromTheGateConfig_InEitherShape()
    {
        using var nested = JsonDocument.Parse("""{"form":{"fields":[{"key":"amount","label":"Amount","type":"Number","required":true,"min":1}]}}""");
        using var flat = JsonDocument.Parse("""{"form":[{"id":"note"}]}""");
        using var none = JsonDocument.Parse("""{"assignee":"a@b.test"}""");

        var fields = GateForms.Read(nested.RootElement);
        fields.Should().ContainSingle();
        fields[0].Should().BeEquivalentTo(new GateFormField("amount", "Amount", "number", true, null, null, 1, null));
        GateForms.Read(flat.RootElement).Single().Should().BeEquivalentTo(new GateFormField("note", "note", "text"));
        GateForms.Read(none.RootElement).Should().BeEmpty();
    }

    [Fact]
    public void DefinitionProblems_NamesEachMistake()
    {
        GateForms.DefinitionProblems(Purchase, "gate").Should().BeEmpty();

        var problems = GateForms.DefinitionProblems(
        [
            new("Amount", "Amount", "number"),
            new("note", "Note", "text"),
            new("note", "Note again", "text"),
            new("kind", "Kind", "select"),
            new("size", "Size", "slider"),
            new("range", "Range", "number", Min: 10, Max: 1),
        ], "approve");

        problems.Should().HaveCount(5);
        problems.Should().Contain(p => p.Contains("'Amount' must be lowercase"));
        problems.Should().Contain(p => p.Contains("'note' is used twice"));
        problems.Should().Contain(p => p.Contains("select field 'kind' has no options"));
        problems.Should().Contain(p => p.Contains("type 'slider'"));
        problems.Should().Contain(p => p.Contains("min 10 above max 1"));
    }

    [Fact]
    public void Check_NormalisesValues_AndDropsUnknownKeys()
    {
        var (values, problems) = GateForms.Check(Purchase, Values("""
            {"amount":"1250.5","cost_centre":"  CC-12 ","budget_line":"Capital","receipts":true,"start":"2026-12-01","cover":"anita@acme.test","extra":"x"}
            """));

        problems.Should().BeEmpty();
        values.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["amount"] = 1250.5, ["cost_centre"] = "CC-12", ["budget_line"] = "Capital", ["receipts"] = true,
            ["start"] = "2026-12-01", ["cover"] = "anita@acme.test",
        });
    }

    [Fact]
    public void Check_ExplainsEveryProblemToTheApprover()
    {
        var (_, problems) = GateForms.Check(Purchase, Values("""
            {"amount":60000,"budget_line":"Travel","receipts":false,"start":"01/12/2026","cover":"not-an-email"}
            """));

        problems.Should().BeEquivalentTo(
            "Approved amount must be at most 50000.",
            "Cost centre is required.",
            "Budget line must be one of: Operating, Capital.",
            "Receipts checked must be ticked to approve.",
            "Start date must be a date (YYYY-MM-DD).",
            "Cover must be an email address.");
    }

    [Fact]
    public void Check_MissingOptionalFields_AreFine_AndUntickedBoxesAreFalse()
    {
        GateFormField[] fields = [new("note", "Note", "textarea"), new("urgent", "Urgent", "checkbox")];

        var (values, problems) = GateForms.Check(fields, null);

        problems.Should().BeEmpty();
        values.Should().BeEquivalentTo(new Dictionary<string, object?> { ["urgent"] = false });
    }
}
