using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Interfaces.Repositories;

namespace Flowamaz.Application.Auth.Scim;

/// <summary>SCIM 2.0 JSON: schema URNs, rendering users and groups, and reading request bodies and filters.</summary>
public static partial class ScimJson
{
    public const string UserSchema = "urn:ietf:params:scim:schemas:core:2.0:User";
    public const string GroupSchema = "urn:ietf:params:scim:schemas:core:2.0:Group";
    public const string ListSchema = "urn:ietf:params:scim:api:messages:2.0:ListResponse";
    public const string PatchSchema = "urn:ietf:params:scim:api:messages:2.0:PatchOp";
    public const string ErrorSchema = "urn:ietf:params:scim:api:messages:2.0:Error";
    public const int MaxPageSize = 100;
    public const int MaxMembersPerRequest = 1000;

    // ── Rendering ───────────────────────────────────────────────────────────────────────

    public static JsonObject User(OrgUser u, string baseUrl)
    {
        var (given, family) = SplitName(u.Name);
        var user = new JsonObject
        {
            ["schemas"] = new JsonArray(UserSchema),
            ["id"] = u.Id.ToString(),
            ["userName"] = u.ScimUserName ?? u.Email,
            ["name"] = new JsonObject { ["formatted"] = u.Name, ["givenName"] = given, ["familyName"] = family },
            ["displayName"] = u.Name,
            ["emails"] = new JsonArray(new JsonObject { ["value"] = u.Email, ["type"] = "work", ["primary"] = true }),
            ["active"] = u.IsActive,
            ["meta"] = Meta("User", u.CreatedAt, u.UpdatedAt, $"{baseUrl}/Users/{u.Id}"),
        };
        if (u.ScimExternalId is not null) user["externalId"] = u.ScimExternalId;
        return user;
    }

    public static JsonObject Group(ScimGroup g, IEnumerable<ScimMemberRef>? members, string baseUrl)
    {
        var group = new JsonObject
        {
            ["schemas"] = new JsonArray(GroupSchema),
            ["id"] = g.Id.ToString(),
            ["displayName"] = g.DisplayName,
            ["meta"] = Meta("Group", g.CreatedAt, g.UpdatedAt, $"{baseUrl}/Groups/{g.Id}"),
        };
        if (g.ExternalId is not null) group["externalId"] = g.ExternalId;
        if (members is not null)
            group["members"] = new JsonArray(members.Select(m => (JsonNode)new JsonObject
            {
                ["value"] = m.OrgUserId.ToString(),
                ["display"] = m.Email,
                ["$ref"] = $"{baseUrl}/Users/{m.OrgUserId}",
            }).ToArray());
        return group;
    }

    public static JsonObject List(IEnumerable<JsonNode> resources, int total, int startIndex)
    {
        var items = resources.ToArray();
        return new JsonObject
        {
            ["schemas"] = new JsonArray(ListSchema),
            ["totalResults"] = total,
            ["startIndex"] = startIndex,
            ["itemsPerPage"] = items.Length,
            ["Resources"] = new JsonArray(items),
        };
    }

    public static JsonObject Error(int status, string? scimType, string detail)
    {
        var error = new JsonObject
        {
            ["schemas"] = new JsonArray(ErrorSchema),
            ["status"] = status.ToString(CultureInfo.InvariantCulture),
            ["detail"] = detail,
        };
        if (scimType is not null) error["scimType"] = scimType;
        return error;
    }

    private static JsonObject Meta(string type, DateTime created, DateTime modified, string location) => new()
    {
        ["resourceType"] = type,
        ["created"] = DateTime.SpecifyKind(created, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
        ["lastModified"] = DateTime.SpecifyKind(modified, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
        ["location"] = location,
    };

    internal static (string? Given, string? Family) SplitName(string name)
    {
        var parts = name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch { 0 => (null, null), 1 => (parts[0], null), _ => (parts[0], parts[1]) };
    }

    // ── Reading ─────────────────────────────────────────────────────────────────────────

    public static JsonElement Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) throw ScimException.Syntax("The request body is empty. Send a SCIM JSON resource.");
        try
        {
            using var doc = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw ScimException.Syntax("The request body must be a JSON object.");
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw ScimException.Syntax("The request body isn't valid JSON.");
        }
    }

    /// <summary>Property lookup ignoring case: SCIM attribute names are case-insensitive (RFC 7643 §2.1).</summary>
    public static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object)
            foreach (var p in obj.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
        value = default;
        return false;
    }

    public static string? String(JsonElement obj, string name) =>
        TryGet(obj, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>A boolean, also accepting "True"/"False" strings (Microsoft Entra sends those).</summary>
    public static bool? Bool(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String when bool.TryParse(value.GetString(), out var b) => b,
        _ => throw ScimException.Invalid("'active' must be true or false."),
    };

    /// <summary>The primary (else work, else first) email in an <c>emails</c> array.</summary>
    public static string? PrimaryEmail(JsonElement emails)
    {
        if (emails.ValueKind != JsonValueKind.Array) return null;
        JsonElement? chosen = null;
        foreach (var e in emails.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            if (TryGet(e, "primary", out var p) && p.ValueKind == JsonValueKind.True) { chosen = e; break; }
            if (chosen is null || String(e, "type") == "work") chosen = e;
        }
        return chosen is { } c ? String(c, "value") : null;
    }

    public static bool IsEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254) return false;
        try { return new MailAddress(value).Address == value; }
        catch (FormatException) { return false; }
    }

    // ── Filters ─────────────────────────────────────────────────────────────────────────

    [GeneratedRegex("""^\s*([A-Za-z][\w.:]*)\s+eq\s+"((?:[^"\\]|\\.)*)"\s*$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex EqFilter();

    /// <summary>Only <c>attribute eq "value"</c> is supported, which is what Okta and Entra send.</summary>
    public static (string Attribute, string Value)? ParseFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return null;
        if (filter.Length > 512) throw ScimException.Filter("The filter is too long.");
        var match = EqFilter().Match(filter);
        if (!match.Success)
            throw ScimException.Filter("Only filters of the form attribute eq \"value\" are supported, e.g. userName eq \"jane@acme.com\".");
        // The value is a JSON string (RFC 7644 §3.4.2.2), so unescape it as one.
        string value;
        try { value = JsonSerializer.Deserialize<string>("\"" + match.Groups[2].Value + "\"") ?? ""; }
        catch (JsonException) { throw ScimException.Filter("The filter value has an invalid escape sequence."); }
        return (match.Groups[1].Value, value);
    }

    public static (ScimUserFilterAttribute, string)? UserFilter(string? filter)
    {
        if (ParseFilter(filter) is not var (attribute, value)) return null;
        return attribute.ToLowerInvariant() switch
        {
            "username" => (ScimUserFilterAttribute.UserName, value),
            "externalid" => (ScimUserFilterAttribute.ExternalId, value),
            "emails.value" or "emails" => (ScimUserFilterAttribute.Email, value),
            _ => throw ScimException.Filter($"Filtering users by '{attribute}' isn't supported. Use userName, externalId or emails.value."),
        };
    }

    public static (ScimGroupFilterAttribute, string)? GroupFilter(string? filter)
    {
        if (ParseFilter(filter) is not var (attribute, value)) return null;
        return attribute.ToLowerInvariant() switch
        {
            "displayname" => (ScimGroupFilterAttribute.DisplayName, value),
            "externalid" => (ScimGroupFilterAttribute.ExternalId, value),
            _ => throw ScimException.Filter($"Filtering groups by '{attribute}' isn't supported. Use displayName or externalId."),
        };
    }

    [GeneratedRegex("""^members\[\s*value\s+eq\s+"([^"]+)"\s*\]$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    public static partial Regex MemberValuePath();
}
