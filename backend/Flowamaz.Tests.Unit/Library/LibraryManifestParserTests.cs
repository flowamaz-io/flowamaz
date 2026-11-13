using System.Text.Json;
using FluentAssertions;
using Flowamaz.Application.Library;

namespace Flowamaz.Tests.Unit.Library;

/// <summary>Library repository folders (FUNCTIONAL.md §23.3–23.4) and semver, as the sync reads them.</summary>
public sealed class LibraryManifestParserTests
{
    private const string Connector = """
        id: invoice-sync
        publisher: acme
        version: 1.3.0
        display_name: Invoice Sync
        category: finance
        tags: [invoices, erp]
        auth: { type: api_key }
        network:
          allowed_domains: [api.acme.test]
        operations:
          - id: create-invoice
            method: POST
            path: /invoices
            timeout_seconds: 30
            retry: true
            label: "007"
        """;

    private const string Changelog = """
        # Changelog

        ## 1.3.0 (2026-11-02)
        - Adds currency to create-invoice.

        ## [1.2.1] - 2026-10-20
        - Fixes retries on 429.

        ## v1.2.0
        Initial release.
        """;

    [Theory]
    [InlineData("acme/invoice-sync")]
    [InlineData("connectors/acme/invoice-sync")]
    public void ParseConnector_ReadsTheManifest_AndKeepsYamlTypesInJson(string path)
    {
        var c = LibraryManifestParser.ParseConnector(new LibraryFolder(path, Connector, Changelog));

        c.ConnectorId.Should().Be("invoice-sync");
        c.PublisherId.Should().Be("acme");
        c.Version.Should().Be("1.3.0");
        c.MajorVersion.Should().Be(1);
        c.DisplayName.Should().Be("Invoice Sync");
        c.Category.Should().Be("finance");
        c.Tags.Should().Equal("invoices", "erp");
        using var json = JsonDocument.Parse(c.ManifestJson);
        var op = json.RootElement.GetProperty("operations")[0];
        op.GetProperty("timeout_seconds").GetInt64().Should().Be(30);
        op.GetProperty("retry").GetBoolean().Should().BeTrue();
        op.GetProperty("label").GetString().Should().Be("007", "a quoted scalar stays a string");
        c.Changelog.Select(e => e.Version).Should().Equal("1.3.0", "1.2.1", "1.2.0");
        c.Changelog[0].Notes.Should().Be("- Adds currency to create-invoice.");
        c.Changelog[0].ReleasedOn.Should().Be(new DateOnly(2026, 11, 2));
        c.Deprecated.Should().BeFalse();
    }

    [Theory]
    [InlineData("acme/invoice-sync", "publisher: someone-else", "doesn't match its folder 'acme'")]
    [InlineData("acme/invoice-sync", "tier: official", "only flowamaz-io publishes official items")]
    [InlineData("acme/other-name", "", "must match its folder name 'other-name'")]
    [InlineData("acme/invoice-sync", "version: 1.3", "is not semver")]
    [InlineData("acme/Invoice_Sync", "", "lowercase, hyphenated names")]
    [InlineData("invoice-sync", "", "expected connectors/{publisher}/{name}/")]
    public void ParseConnector_Refuses_SpoofedOrMalformedFolders(string path, string change, string expected)
    {
        var yaml = change.Length == 0 ? Connector
            : change.StartsWith("version") ? Connector.Replace("version: 1.3.0", change)
            : Connector.Replace("publisher: acme", change);

        var act = () => LibraryManifestParser.ParseConnector(new LibraryFolder(path, yaml));

        act.Should().Throw<LibraryManifestException>().WithMessage($"*{expected}*");
    }

    [Fact]
    public void ParseConnector_RefusesAnchorsAndAliases()
    {
        var yaml = Connector + "\ndefaults: &d { retry: true }\nother: *d\n";

        var act = () => LibraryManifestParser.ParseConnector(new LibraryFolder("acme/invoice-sync", yaml));

        act.Should().Throw<LibraryManifestException>().WithMessage("*anchors and aliases*");
    }

    [Theory]
    [InlineData("deprecated: true", "Deprecated by its publisher.")]
    [InlineData("deprecated: Use acme/invoices instead", "Use acme/invoices instead")]
    [InlineData("deprecated: { message: Moving to v2 }", "Moving to v2")]
    public void ParseConnector_ReadsDeprecation(string line, string note)
    {
        var c = LibraryManifestParser.ParseConnector(new LibraryFolder("acme/invoice-sync", Connector + "\n" + line));

        c.Deprecated.Should().BeTrue();
        c.DeprecationNote.Should().Be(note);
    }

    [Fact]
    public void ParseTemplate_NeedsWorkflowYaml_AndPrefixesCommunitySlugs()
    {
        const string manifest = "name: Expense claims\nversion: 2.0.0\ncategory: Finance\ndescription: Claims with receipts\n";
        var official = LibraryManifestParser.ParseTemplate(new LibraryFolder("templates/flowamaz-io/expense-claims", manifest, null, "apiVersion: flowamaz/v1"));
        var community = LibraryManifestParser.ParseTemplate(new LibraryFolder("acme/expense-claims", manifest, null, "apiVersion: flowamaz/v1"));

        official.Slug.Should().Be("expense-claims");
        community.Slug.Should().Be("acme-expense-claims");
        community.Name.Should().Be("Expense claims");
        community.Version.Should().Be("2.0.0");
        var act = () => LibraryManifestParser.ParseTemplate(new LibraryFolder("acme/expense-claims", manifest));
        act.Should().Throw<LibraryManifestException>().WithMessage("*workflow.yaml is missing*");
    }

    [Theory]
    [InlineData("1.10.0", "1.9.9", true)]
    [InlineData("2.0.0", "1.99.99", true)]
    [InlineData("1.0.0", "1.0.0-beta.1", true)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.1", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.2.0", "1.10.0", false)]
    [InlineData("not-a-version", "1.0.0", false)]
    [InlineData("1.0.0", "garbage", true)]
    public void SemVersion_ComparesNumerically(string candidate, string current, bool newer) =>
        SemVersion.IsNewer(candidate, current).Should().Be(newer);

    [Fact]
    public void Between_ReturnsTheNotesAfterTheInstalledVersion()
    {
        var log = LibraryManifestParser.ParseChangelog(Changelog);

        LibraryManifestParser.Between(log, "1.2.0", "1.3.0").Select(e => e.Version).Should().Equal("1.3.0", "1.2.1");
        LibraryManifestParser.Between(log, "1.3.0", "1.3.0").Should().BeEmpty();
    }

    [Fact]
    public void DeclaredId_ReadsIdOrName_WhenItIsASlug()
    {
        LibraryManifestParser.DeclaredId("id: my-connector\nversion: 1.0.0").Should().Be("my-connector");
        LibraryManifestParser.DeclaredId("name: my-connector").Should().Be("my-connector");
        LibraryManifestParser.DeclaredId("name: My Connector").Should().BeNull();
        LibraryManifestParser.DeclaredId(": : broken").Should().BeNull();
    }
}
