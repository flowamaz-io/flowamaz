using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flowamaz.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Flowamaz.Application.Library;

/// <summary>A semantic version (major.minor.patch, optional -prerelease). Build metadata is ignored.</summary>
public readonly partial record struct SemVersion(int Major, int Minor, int Patch, string? Prerelease) : IComparable<SemVersion>
{
    public static bool TryParse(string? text, out SemVersion version)
    {
        version = default;
        var m = SemVerRegex().Match(text?.Trim() ?? "");
        if (!m.Success) return false;
        version = new SemVersion(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
            m.Groups[4].Success ? m.Groups[4].Value : null);
        return true;
    }

    /// <summary>Major of a version string, or 1 when it isn't semver (pre-existing rows).</summary>
    public static int MajorOf(string? text) => TryParse(text, out var v) ? v.Major : 1;

    /// <summary>True when <paramref name="candidate"/> is a newer version than <paramref name="current"/>.</summary>
    public static bool IsNewer(string? candidate, string? current) =>
        TryParse(candidate, out var a) && (!TryParse(current, out var b) || a.CompareTo(b) > 0);

    public int CompareTo(SemVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        // A release sorts after its prereleases (1.0.0-beta < 1.0.0).
        if (Prerelease == other.Prerelease) return 0;
        if (Prerelease is null) return 1;
        if (other.Prerelease is null) return -1;
        return string.CompareOrdinal(Prerelease, other.Prerelease);
    }

    public override string ToString() => Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";

    [GeneratedRegex(@"^v?(\d{1,6})\.(\d{1,6})\.(\d{1,6})(?:-([0-9A-Za-z.\-]{1,64}))?(?:\+[0-9A-Za-z.\-]+)?$")]
    private static partial Regex SemVerRegex();
}

/// <summary>A connector folder of the Library repository, read and checked.</summary>
public sealed record ParsedConnector(
    string ConnectorId,
    string PublisherId,
    string DisplayName,
    string Version,
    int MajorVersion,
    string Category,
    string[] Tags,
    string ManifestJson,
    IReadOnlyList<ChangelogEntry> Changelog,
    string? DeprecationNote,
    bool Deprecated,
    string Path);

/// <summary>A template folder of the Library repository, read and checked.</summary>
public sealed record ParsedTemplate(
    string Slug,
    string Name,
    string PublisherId,
    string Version,
    string Category,
    string Description,
    string[] Tags,
    string WorkflowYaml,
    string? DeprecationNote,
    bool Deprecated,
    string Path);

/// <summary>A folder the sync could not use; it is skipped and the rest of the Library still syncs.</summary>
public sealed class LibraryManifestException(string message) : Exception(message);

/// <summary>
/// Reads Library repository folders (FUNCTIONAL.md §23.3–23.4) with YamlDotNet: <c>connector.yaml</c> (+
/// CHANGELOG.md) and <c>template.yaml</c> + <c>workflow.yaml</c>. The folder path names the publisher
/// (<c>{publisher}/{name}/</c>, optionally under <c>connectors/</c> or <c>templates/</c>); a manifest that claims
/// another publisher, or claims to be official when its publisher isn't flowamaz-io, is refused.
/// </summary>
public static partial class LibraryManifestParser
{
    public const string OfficialPublisher = "flowamaz-io";
    private static readonly string[] ConnectorCategories = ["generic", "database", "messaging", "devops", "productivity", "finance", "hr", "crm", "storage", "ai"];

    /// <summary>The <c>id</c> (or <c>name</c>) a manifest declares, when it is a slug; null otherwise.</summary>
    public static string? DeclaredId(string yaml)
    {
        try
        {
            var root = Load(yaml, "connector.yaml", "manifest");
            var id = Scalar(root, "id") ?? Scalar(root, "name");
            return id is not null && SlugRegex().IsMatch(id) ? id : null;
        }
        catch (LibraryManifestException)
        {
            return null;
        }
    }

    public static ParsedConnector ParseConnector(LibraryFolder folder)
    {
        var (publisher, name) = PublisherAndName(folder.Path, "connectors");
        var root = Load(folder.ManifestYaml, "connector.yaml", folder.Path);

        var id = Scalar(root, "id") ?? Scalar(root, "name") ?? name;
        if (!SlugRegex().IsMatch(id))
            throw new LibraryManifestException($"{folder.Path}/connector.yaml: id '{id}' must be lowercase letters, numbers and hyphens.");
        if (!id.Equals(name, StringComparison.Ordinal))
            throw new LibraryManifestException($"{folder.Path}/connector.yaml: id '{id}' must match its folder name '{name}'.");
        CheckPublisher(root, publisher, folder.Path, "connector.yaml");

        var version = Scalar(root, "version") ?? throw new LibraryManifestException($"{folder.Path}/connector.yaml: version is missing (semver, e.g. 1.0.0).");
        if (!SemVersion.TryParse(version, out var semver))
            throw new LibraryManifestException($"{folder.Path}/connector.yaml: version '{version}' is not semver (e.g. 1.0.0).");

        var category = (Scalar(root, "category") ?? "generic").ToLowerInvariant();
        if (!ConnectorCategories.Contains(category)) category = "generic";
        var (deprecated, note) = Deprecation(root);

        return new ParsedConnector(
            ConnectorId: id,
            PublisherId: publisher,
            DisplayName: Scalar(root, "display_name") ?? Scalar(root, "title") ?? Humanise(id),
            Version: semver.ToString(),
            MajorVersion: semver.Major,
            Category: category,
            Tags: Strings(root, "tags"),
            ManifestJson: ToJson(root)!.ToJsonString(),
            Changelog: ParseChangelog(folder.ChangelogMarkdown),
            DeprecationNote: note,
            Deprecated: deprecated,
            Path: folder.Path);
    }

    public static ParsedTemplate ParseTemplate(LibraryFolder folder)
    {
        var (publisher, name) = PublisherAndName(folder.Path, "templates");
        var root = Load(folder.ManifestYaml, "template.yaml", folder.Path);
        CheckPublisher(root, publisher, folder.Path, "template.yaml");

        var version = Scalar(root, "version") ?? throw new LibraryManifestException($"{folder.Path}/template.yaml: version is missing (semver, e.g. 1.0.0).");
        if (!SemVersion.TryParse(version, out var semver))
            throw new LibraryManifestException($"{folder.Path}/template.yaml: version '{version}' is not semver (e.g. 1.0.0).");
        if (string.IsNullOrWhiteSpace(folder.WorkflowYaml))
            throw new LibraryManifestException($"{folder.Path}: workflow.yaml is missing. A template needs a complete workflow definition.");
        Load(folder.WorkflowYaml, "workflow.yaml", folder.Path); // must at least be YAML; the workflow validator checks it on install

        var (deprecated, note) = Deprecation(root);
        // Official templates keep their short slug (purchase-approval); community ones are prefixed by publisher.
        var slug = publisher == OfficialPublisher ? name : $"{publisher}-{name}";
        return new ParsedTemplate(
            Slug: slug,
            Name: Scalar(root, "name") is { } n && !SlugRegex().IsMatch(n) ? n : Scalar(root, "title") ?? Humanise(name),
            PublisherId: publisher,
            Version: semver.ToString(),
            Category: Scalar(root, "category") ?? "Custom",
            Description: Scalar(root, "description") ?? "",
            Tags: Strings(root, "tags"),
            WorkflowYaml: folder.WorkflowYaml!,
            DeprecationNote: note,
            Deprecated: deprecated,
            Path: folder.Path);
    }

    /// <summary>
    /// Release notes from CHANGELOG.md: <c>## 1.2.0 (2026-11-01)</c>, <c>## [1.2.0] - 2026-11-01</c> or
    /// <c>## v1.2.0</c> headings, each followed by its notes. Newest first, as written.
    /// </summary>
    public static IReadOnlyList<ChangelogEntry> ParseChangelog(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];
        var entries = new List<ChangelogEntry>();
        string? version = null;
        DateOnly? date = null;
        var notes = new List<string>();
        void Flush()
        {
            if (version is not null) entries.Add(new ChangelogEntry(version, string.Join("\n", notes).Trim(), date));
            notes.Clear();
        }
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var heading = ChangelogHeadingRegex().Match(raw.Trim());
            if (heading.Success && SemVersion.TryParse(heading.Groups[1].Value, out var v))
            {
                Flush();
                version = v.ToString();
                date = DateOnly.TryParseExact(heading.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
                continue;
            }
            if (version is not null && raw.Trim().Length > 0) notes.Add(raw.Trim());
        }
        Flush();
        return entries.Take(50).ToList();
    }

    /// <summary>Changelog entries newer than <paramref name="fromVersion"/>, up to and including <paramref name="toVersion"/>.</summary>
    public static IReadOnlyList<ChangelogEntry> Between(IReadOnlyList<ChangelogEntry> changelog, string fromVersion, string toVersion) =>
        changelog.Where(e => SemVersion.IsNewer(e.Version, fromVersion) && !SemVersion.IsNewer(e.Version, toVersion)).ToList();

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private static (string Publisher, string Name) PublisherAndName(string path, string root)
    {
        var parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 && parts[0] == root) parts = parts[1..];
        if (parts.Length != 2 || !SlugRegex().IsMatch(parts[0]) || !SlugRegex().IsMatch(parts[1]))
            throw new LibraryManifestException($"{path}: expected {root}/{{publisher}}/{{name}}/ with lowercase, hyphenated names.");
        return (parts[0], parts[1]);
    }

    private static void CheckPublisher(YamlMappingNode root, string publisher, string path, string file)
    {
        var declared = Scalar(root, "publisher");
        if (declared is not null && declared != publisher)
            throw new LibraryManifestException($"{path}/{file}: publisher '{declared}' doesn't match its folder '{publisher}'.");
        var tier = Scalar(root, "tier");
        var claimsOfficial = string.Equals(tier, "official", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(Scalar(root, "official"), "true", StringComparison.OrdinalIgnoreCase);
        if (claimsOfficial && publisher != OfficialPublisher)
            throw new LibraryManifestException($"{path}/{file}: only {OfficialPublisher} publishes official items.");
    }

    private static (bool, string?) Deprecation(YamlMappingNode root)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("deprecated"), out var node)) return (false, null);
        return node switch
        {
            YamlScalarNode s when bool.TryParse(s.Value, out var b) => (b, b ? "Deprecated by its publisher." : null),
            YamlScalarNode s when !string.IsNullOrWhiteSpace(s.Value) => (true, s.Value!.Trim()),
            YamlMappingNode m => (true, Scalar(m, "message") ?? Scalar(m, "reason") ?? "Deprecated by its publisher."),
            _ => (false, null),
        };
    }

    private static YamlMappingNode Load(string yaml, string file, string path)
    {
        try
        {
            if (HasAnchors(yaml))
                throw new LibraryManifestException($"{path}/{file}: YAML anchors and aliases aren't allowed in Library manifests.");
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
                throw new LibraryManifestException($"{path}/{file}: expected a YAML mapping at the top level.");
            return root;
        }
        catch (YamlException ex)
        {
            throw new LibraryManifestException($"{path}/{file}: not valid YAML (line {ex.Start.Line}): {ex.Message}");
        }
    }

    private static bool HasAnchors(string yaml)
    {
        var parser = new Parser(new StringReader(yaml));
        while (parser.MoveNext())
        {
            switch (parser.Current)
            {
                case YamlDotNet.Core.Events.AnchorAlias:
                case YamlDotNet.Core.Events.NodeEvent { Anchor.IsEmpty: false }:
                    return true;
            }
        }
        return false;
    }

    private static string? Scalar(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s && !string.IsNullOrWhiteSpace(s.Value)
            ? s.Value!.Trim()
            : null;

    private static string[] Strings(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlSequenceNode seq
            ? seq.Children.OfType<YamlScalarNode>().Select(s => s.Value?.Trim()).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).Take(20).ToArray()
            : [];

    /// <summary>YAML → JSON, keeping plain scalars typed (numbers, booleans, null) and quoted ones as strings.</summary>
    public static JsonNode? ToJson(YamlNode node) => node switch
    {
        YamlMappingNode m => new JsonObject(m.Children
            .Where(kv => kv.Key is YamlScalarNode)
            .Select(kv => KeyValuePair.Create(((YamlScalarNode)kv.Key).Value ?? "", ToJson(kv.Value)))),
        YamlSequenceNode s => new JsonArray(s.Children.Select(ToJson).ToArray()),
        YamlScalarNode sc => Scalar(sc),
        _ => null,
    };

    private static JsonNode? Scalar(YamlScalarNode s)
    {
        var v = s.Value ?? "";
        if (s.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted or ScalarStyle.Literal or ScalarStyle.Folded)
            return JsonValue.Create(v);
        if (v is "" or "~" or "null" or "Null" or "NULL") return null;
        if (v is "true" or "True" or "TRUE") return JsonValue.Create(true);
        if (v is "false" or "False" or "FALSE") return JsonValue.Create(false);
        if (long.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) return JsonValue.Create(l);
        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !double.IsInfinity(d)) return JsonValue.Create(d);
        return JsonValue.Create(v);
    }

    private static string Humanise(string slug) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('-', ' '));

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex SlugRegex();

    [GeneratedRegex(@"^#{2,3}\s*\[?(v?\d+\.\d+\.\d+[^\]\s)]*)\]?\s*(?:[-–(]\s*(\d{4}-\d{2}-\d{2})\)?)?")]
    private static partial Regex ChangelogHeadingRegex();
}

/// <summary>The files of one Library folder the parser needs.</summary>
public sealed record LibraryFolder(string Path, string ManifestYaml, string? ChangelogMarkdown = null, string? WorkflowYaml = null);
