using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Flowamaz.Core.Workflow;

/// <summary>One field an approver fills in at a human gate (FUNCTIONAL.md §23.2, Library Forms).</summary>
public sealed record GateFormField(
    string Key,
    string Label,
    string Type,
    bool Required = false,
    IReadOnlyList<string>? Options = null,
    string? Help = null,
    double? Min = null,
    double? Max = null);

/// <summary>
/// Human-gate forms: a gate's <c>config.form.fields</c> (inline YAML, or copied from a Library form) are shown to
/// the approver, checked here when they approve, and passed on as the gate's output
/// (<c>{{ gate-id.output.form.field_key }}</c>).
/// </summary>
public static partial class GateForms
{
    public static readonly string[] FieldTypes = ["text", "textarea", "number", "date", "select", "checkbox", "email"];
    public const int MaxFields = 30;
    public const int MaxTextLength = 2000;
    public const int MaxOptions = 50;

    /// <summary>The fields of a gate config's <c>form</c>, or an empty list when the gate has none. Throws nothing.</summary>
    public static IReadOnlyList<GateFormField> Read(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object || !config.TryGetProperty("form", out var form)) return [];
        var fields = form.ValueKind switch
        {
            JsonValueKind.Object when form.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array => f,
            JsonValueKind.Array => form,
            _ => default,
        };
        if (fields.ValueKind != JsonValueKind.Array) return [];
        return fields.EnumerateArray().Where(f => f.ValueKind == JsonValueKind.Object).Select(ReadField).Take(MaxFields).ToList();
    }

    private static GateFormField ReadField(JsonElement f)
    {
        string? Str(string name) => f.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;
        double? Num(string name) => f.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        var key = Str("key") ?? Str("id") ?? "";
        var options = f.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array
            ? o.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToList()
            : null;
        return new GateFormField(
            key,
            Str("label") ?? key,
            (Str("type") ?? "text").ToLowerInvariant(),
            f.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True,
            options,
            Str("help"),
            Num("min"),
            Num("max"));
    }

    /// <summary>What's wrong with a form definition, as fix-it messages (empty = fine).</summary>
    public static IReadOnlyList<string> DefinitionProblems(IReadOnlyList<GateFormField> fields, string where)
    {
        var problems = new List<string>();
        if (fields.Count > MaxFields) problems.Add($"{where} has more than {MaxFields} form fields. Split it across two gates.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in fields)
        {
            if (!KeyRegex().IsMatch(f.Key))
                problems.Add($"{where}: form field key '{f.Key}' must be lowercase letters, numbers and underscores, starting with a letter (e.g. cost_centre).");
            else if (!seen.Add(f.Key))
                problems.Add($"{where}: form field key '{f.Key}' is used twice. Give each field its own key.");
            if (!FieldTypes.Contains(f.Type))
                problems.Add($"{where}: form field '{f.Key}' has type '{f.Type}'. Use one of: {string.Join(", ", FieldTypes)}.");
            if (f.Type == "select" && (f.Options is null || f.Options.Count == 0))
                problems.Add($"{where}: select field '{f.Key}' has no options. Add options: [..].");
            if (f.Options is { Count: > MaxOptions })
                problems.Add($"{where}: field '{f.Key}' has more than {MaxOptions} options.");
            if (f.Min is { } min && f.Max is { } max && min > max)
                problems.Add($"{where}: field '{f.Key}' has min {min} above max {max}.");
            if (string.IsNullOrWhiteSpace(f.Label) || f.Label.Length > 200)
                problems.Add($"{where}: field '{f.Key}' needs a label under 200 characters.");
        }
        return problems;
    }

    /// <summary>
    /// Checks an approver's values against the form and returns them normalised (numbers as numbers, checkboxes
    /// as booleans, unknown keys dropped), or the problems, phrased for the approver.
    /// </summary>
    public static (Dictionary<string, object?> Values, IReadOnlyList<string> Problems) Check(
        IReadOnlyList<GateFormField> fields, IReadOnlyDictionary<string, JsonElement>? submitted)
    {
        var values = new Dictionary<string, object?>();
        var problems = new List<string>();
        submitted ??= new Dictionary<string, JsonElement>();
        foreach (var f in fields)
        {
            var has = submitted.TryGetValue(f.Key, out var raw) && raw.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                      && !(raw.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(raw.GetString()));
            if (!has)
            {
                if (f.Type == "checkbox") values[f.Key] = false;
                if (f.Required && f.Type != "checkbox") problems.Add($"{f.Label} is required.");
                else if (f.Required && f.Type == "checkbox") problems.Add($"{f.Label} must be ticked to approve.");
                continue;
            }
            switch (f.Type)
            {
                case "number":
                    double number;
                    if (raw.ValueKind == JsonValueKind.Number) number = raw.GetDouble();
                    else if (raw.ValueKind == JsonValueKind.String && double.TryParse(raw.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) number = parsed;
                    else { problems.Add($"{f.Label} must be a number."); break; }
                    if (f.Min is { } min && number < min) problems.Add($"{f.Label} must be at least {min.ToString(CultureInfo.InvariantCulture)}.");
                    else if (f.Max is { } max && number > max) problems.Add($"{f.Label} must be at most {max.ToString(CultureInfo.InvariantCulture)}.");
                    else values[f.Key] = number;
                    break;
                case "checkbox":
                    if (raw.ValueKind is JsonValueKind.True or JsonValueKind.False) values[f.Key] = raw.GetBoolean();
                    else problems.Add($"{f.Label} must be ticked or not (true/false).");
                    if (f.Required && values.GetValueOrDefault(f.Key) is false) problems.Add($"{f.Label} must be ticked to approve.");
                    break;
                case "date":
                    if (raw.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(raw.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        values[f.Key] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    else problems.Add($"{f.Label} must be a date (YYYY-MM-DD).");
                    break;
                case "select":
                    var choice = raw.ValueKind == JsonValueKind.String ? raw.GetString()!.Trim() : raw.ToString();
                    if (f.Options?.Contains(choice) == true) values[f.Key] = choice;
                    else problems.Add($"{f.Label} must be one of: {string.Join(", ", f.Options ?? [])}.");
                    break;
                case "email":
                    var email = raw.ValueKind == JsonValueKind.String ? raw.GetString()!.Trim() : "";
                    if (EmailRegex().IsMatch(email) && email.Length <= 254) values[f.Key] = email;
                    else problems.Add($"{f.Label} must be an email address.");
                    break;
                default:
                    var text = raw.ValueKind == JsonValueKind.String ? raw.GetString()!.Trim() : raw.ToString();
                    if (text.Length > MaxTextLength) problems.Add($"{f.Label} is longer than {MaxTextLength} characters.");
                    else values[f.Key] = text;
                    break;
            }
        }
        return (values, problems);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$")]
    private static partial Regex KeyRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
