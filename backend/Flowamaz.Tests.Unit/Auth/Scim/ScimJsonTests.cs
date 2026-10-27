using System.Text.Json;
using FluentAssertions;
using Flowamaz.Application.Auth.Scim;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Interfaces.Repositories;

namespace Flowamaz.Tests.Unit.Auth.Scim;

/// <summary>SCIM request parsing: the filters Okta and Entra send, Entra's string booleans, emails, and rendering.</summary>
public class ScimJsonTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData("userName eq \"jane@acme.com\"", ScimUserFilterAttribute.UserName, "jane@acme.com")]
    [InlineData("USERNAME EQ \"Jane@Acme.com\"", ScimUserFilterAttribute.UserName, "Jane@Acme.com")]
    [InlineData("externalId eq \"00u1\"", ScimUserFilterAttribute.ExternalId, "00u1")]
    [InlineData("emails.value eq \"a@b.co\"", ScimUserFilterAttribute.Email, "a@b.co")]
    [InlineData("userName eq \"with \\\"quote\\\"\"", ScimUserFilterAttribute.UserName, "with \"quote\"")]
    public void User_filters_are_read(string filter, ScimUserFilterAttribute attribute, string value) =>
        ScimJson.UserFilter(filter).Should().Be((attribute, value));

    [Theory]
    [InlineData("userName co \"a\"")]
    [InlineData("userName eq \"a\" and active eq true")]
    [InlineData("title eq \"x\"")]
    public void Unsupported_filters_are_refused(string filter) =>
        FluentActions.Invoking(() => ScimJson.UserFilter(filter)).Should().Throw<ScimException>().Which.ScimType.Should().Be("invalidFilter");

    [Theory]
    [InlineData("userName eq \"\\x4\"")]
    [InlineData("userName eq \"\\c\"")]
    public void Bad_escapes_are_a_filter_error_not_a_crash(string filter) =>
        FluentActions.Invoking(() => ScimJson.UserFilter(filter)).Should().Throw<ScimException>().Which.ScimType.Should().Be("invalidFilter");

    [Fact]
    public void No_filter_means_everything() => ScimJson.UserFilter(null).Should().BeNull();

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"False\"", false)]
    [InlineData("\"True\"", true)]
    public void Booleans_accept_entras_strings(string json, bool expected) => ScimJson.Bool(J(json)).Should().Be(expected);

    [Fact]
    public void Anything_else_is_not_a_boolean() =>
        FluentActions.Invoking(() => ScimJson.Bool(J("\"yes\""))).Should().Throw<ScimException>();

    [Fact]
    public void The_primary_email_wins_then_work_then_the_first()
    {
        ScimJson.PrimaryEmail(J("""[{"value":"home@x.co","type":"home"},{"value":"work@x.co","type":"work"}]""")).Should().Be("work@x.co");
        ScimJson.PrimaryEmail(J("""[{"value":"work@x.co","type":"work"},{"value":"main@x.co","primary":true}]""")).Should().Be("main@x.co");
        ScimJson.PrimaryEmail(J("""[{"value":"only@x.co"}]""")).Should().Be("only@x.co");
    }

    [Fact]
    public void Attribute_names_are_case_insensitive() => ScimJson.String(J("""{"USERNAME":"a"}"""), "userName").Should().Be("a");

    [Fact]
    public void A_user_renders_with_schema_email_and_location()
    {
        var user = new OrgUser { Email = "jane@acme.com", Name = "Jane Doe", IsActive = true, ScimExternalId = "00u1" };
        var json = ScimJson.User(user, "https://app/api/scim/v2");
        json["userName"]!.GetValue<string>().Should().Be("jane@acme.com");
        json["name"]!["familyName"]!.GetValue<string>().Should().Be("Doe");
        json["externalId"]!.GetValue<string>().Should().Be("00u1");
        json["meta"]!["location"]!.GetValue<string>().Should().Be($"https://app/api/scim/v2/Users/{user.Id}");
    }

    [Fact]
    public void Member_value_paths_are_recognised() =>
        ScimJson.MemberValuePath().Match("members[value eq \"2819c223\"]").Groups[1].Value.Should().Be("2819c223");
}
