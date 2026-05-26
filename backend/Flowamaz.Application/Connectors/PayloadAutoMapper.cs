using System.Text.Json;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors;

public sealed class PayloadAutoMapper : IPayloadAutoMapper
{
    private readonly ILogger<PayloadAutoMapper> _log;

    public PayloadAutoMapper(ILogger<PayloadAutoMapper> log) => _log = log;

    public Task<AutoMapResult> MapAsync(
        JsonDocument sampleJson,
        JsonElement targetSchema,
        Guid workspaceId,
        CancellationToken ct)
    {
        _log.LogInformation("PayloadAutoMapper.MapAsync entry workspaceId={WorkspaceId}", workspaceId);

        // 1. Extract leaf paths from sample JSON
        var leafPaths = new List<(string Path, string Value)>();
        ExtractLeafPaths(sampleJson.RootElement, string.Empty, leafPaths);

        // 2. Extract required fields from target schema
        var requiredFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allProperties = new List<string>();

        if (targetSchema.ValueKind == JsonValueKind.Object)
        {
            if (targetSchema.TryGetProperty("required", out var requiredArr)
                && requiredArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in requiredArr.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String)
                        requiredFields.Add(item.GetString()!);
            }

            if (targetSchema.TryGetProperty("properties", out var props)
                && props.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in props.EnumerateObject())
                    allProperties.Add(prop.Name);
            }
        }

        // Focus on required fields first, then the rest
        var fieldsToMap = requiredFields.Count > 0
            ? requiredFields.ToList()
            : allProperties;

        // 3. Map each field
        var mappings = new List<FieldMapping>();
        var unmapped = new List<string>();

        foreach (var field in fieldsToMap)
        {
            var (bestPath, bestConfidence) = FindBestMatch(field, leafPaths);

            if (bestPath is not null && bestConfidence >= 0.3)
            {
                mappings.Add(new FieldMapping(
                    field,
                    $"{{{{ variables.{bestPath} }}}}",
                    bestConfidence));
            }
            else
            {
                unmapped.Add(field);
            }
        }

        var result = new AutoMapResult(mappings, unmapped);
        _log.LogInformation("PayloadAutoMapper.MapAsync exit mappings={Count} unmapped={Unmapped}",
            mappings.Count, unmapped.Count);
        return Task.FromResult(result);
    }

    private static (string? Path, double Confidence) FindBestMatch(
        string targetField,
        IReadOnlyList<(string Path, string Value)> leafPaths)
    {
        string? bestPath = null;
        double bestConfidence = 0.0;
        var normalizedTarget = Normalize(targetField);

        foreach (var (path, _) in leafPaths)
        {
            double confidence;
            var leafName = GetLeafName(path);
            var normalizedLeaf = Normalize(leafName);

            if (string.Equals(leafName, targetField, StringComparison.Ordinal))
            {
                // Exact match
                confidence = 1.0;
            }
            else if (string.Equals(normalizedLeaf, normalizedTarget, StringComparison.Ordinal))
            {
                // Normalized match (camelCase ↔ snake_case)
                confidence = 0.9;
            }
            else
            {
                // Levenshtein fallback
                var maxLen = Math.Max(normalizedLeaf.Length, normalizedTarget.Length);
                if (maxLen == 0)
                {
                    confidence = 1.0;
                }
                else
                {
                    var distance = LevenshteinDistance(normalizedLeaf, normalizedTarget);
                    confidence = Math.Max(0.0, 1.0 - (double)distance / maxLen);
                }
            }

            if (confidence > bestConfidence)
            {
                bestConfidence = confidence;
                bestPath = path;
            }
        }

        return (bestPath, bestConfidence);
    }

    private static string GetLeafName(string path)
    {
        // For "line_items[0].amount" → "amount"
        // For "contact_id" → "contact_id"
        var lastDot = path.LastIndexOf('.');
        var name = lastDot >= 0 ? path[(lastDot + 1)..] : path;
        // Strip array index suffix like "[0]"
        var bracketIdx = name.IndexOf('[');
        return bracketIdx >= 0 ? name[..bracketIdx] : name;
    }

    private static void ExtractLeafPaths(
        JsonElement element,
        string prefix,
        List<(string Path, string Value)> results)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var childPath = string.IsNullOrEmpty(prefix)
                        ? prop.Name
                        : $"{prefix}.{prop.Name}";
                    ExtractLeafPaths(prop.Value, childPath, results);
                }
                break;

            case JsonValueKind.Array:
                // Take only first element to infer structure
                var enumerator = element.EnumerateArray();
                if (enumerator.MoveNext())
                    ExtractLeafPaths(enumerator.Current, $"{prefix}[0]", results);
                break;

            default:
                results.Add((prefix, element.ToString()));
                break;
        }
    }

    private static string Normalize(string name) =>
        name.ToLowerInvariant().Replace("_", "").Replace("-", "");

    private static int LevenshteinDistance(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                dp[i, j] = a[i - 1] == b[j - 1]
                    ? dp[i - 1, j - 1]
                    : 1 + Math.Min(dp[i - 1, j - 1], Math.Min(dp[i - 1, j], dp[i, j - 1]));
        return dp[a.Length, b.Length];
    }
}
