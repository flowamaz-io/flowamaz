using System.Text.Json;
using FluentAssertions;
using Flowamaz.Application.Connectors;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flowamaz.Tests.Unit.Connectors;

public sealed class PayloadAutoMapperTests
{
    private static PayloadAutoMapper Create() =>
        new(NullLogger<PayloadAutoMapper>.Instance);

    private static JsonDocument ParseSample(string json) => JsonDocument.Parse(json);
    private static JsonElement ParseSchema(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task CamelCaseField_MatchesSnakeCaseSchemaField_AtHighConfidence()
    {
        // Sample has camelCase "contactId"; schema requires snake_case "contact_id"
        using var sample = ParseSample("""{"contactId": "C001"}""");
        var schema = ParseSchema("""
            {
              "properties": { "contact_id": { "type": "string" } },
              "required": ["contact_id"]
            }
            """);

        var result = await Create().MapAsync(sample, schema, Guid.NewGuid(), default);

        result.Mappings.Should().HaveCount(1);
        result.Mappings[0].FieldName.Should().Be("contact_id");
        result.Mappings[0].Confidence.Should().BeGreaterThanOrEqualTo(0.8);
        result.UnmappedFields.Should().BeEmpty();
    }

    [Fact]
    public async Task ExactMatch_ReturnsConfidenceOne()
    {
        // Sample and schema both use "contact_id"
        using var sample = ParseSample("""{"contact_id": "C001"}""");
        var schema = ParseSchema("""
            {
              "properties": { "contact_id": { "type": "string" } },
              "required": ["contact_id"]
            }
            """);

        var result = await Create().MapAsync(sample, schema, Guid.NewGuid(), default);

        result.Mappings.Should().HaveCount(1);
        result.Mappings[0].FieldName.Should().Be("contact_id");
        result.Mappings[0].Confidence.Should().Be(1.0);
        result.Mappings[0].SuggestedExpression.Should().Contain("contact_id");
        result.UnmappedFields.Should().BeEmpty();
    }

    [Fact]
    public async Task NoMatch_FieldAppearsInUnmappedList()
    {
        // Sample has "foo", schema requires "contact_id" — no overlap
        using var sample = ParseSample("""{"foo": "bar"}""");
        var schema = ParseSchema("""
            {
              "properties": { "contact_id": { "type": "string" } },
              "required": ["contact_id"]
            }
            """);

        var result = await Create().MapAsync(sample, schema, Guid.NewGuid(), default);

        result.Mappings.Should().BeEmpty();
        result.UnmappedFields.Should().Contain("contact_id");
    }
}
