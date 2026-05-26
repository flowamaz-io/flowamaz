using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Creation;

public sealed partial class ConversationImportService : IConversationImportService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private const string ExtractionSystemPrompt = """
        You are a business process extraction specialist. Extract a workflow from the conversation.
        Identify: process steps (in order), decision points, approvers, systems mentioned, conditions/thresholds.
        Return JSON ONLY:
        {
          "workflow_name": "string",
          "summary": "one sentence description",
          "steps": ["step1", "step2"],
          "approvers": ["name or role"],
          "systems": ["system names"],
          "trigger": "what starts it",
          "rules": "thresholds, conditions",
          "confidence": 0.0-1.0
        }
        """;

    private readonly IAiCompletionService _ai;
    private readonly IModelResolutionService _models;
    private readonly IAiTokenMeteringService _metering;
    private readonly INlYamlGenerationService _nlGenerator;
    private readonly ILogger<ConversationImportService> _log;

    public ConversationImportService(
        IAiCompletionService ai,
        IModelResolutionService models,
        IAiTokenMeteringService metering,
        INlYamlGenerationService nlGenerator,
        ILogger<ConversationImportService> log)
    {
        _ai = ai;
        _models = models;
        _metering = metering;
        _nlGenerator = nlGenerator;
        _log = log;
    }

    public async Task<ConversationImportResult> ImportAsync(string conversationText, string sourceType, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        _log.LogInformation("ConversationImportService.ImportAsync entry workspaceId={WorkspaceId} source={Source}", workspaceId, sourceType);

        var normalised = NormaliseConversation(conversationText, sourceType);
        var modelConfig = await _models.ResolveModelConfigAsync(AiFunctionIds.NlYaml, workspaceId, cancellationToken);
        var result = await _ai.CompleteAsync(modelConfig, ExtractionSystemPrompt, normalised, cancellationToken);

        _metering.RecordUsage(AiFunctionIds.NlYaml, modelConfig.ModelId, modelConfig.Provider,
            null, workspaceId, result.TokensInput, result.TokensOutput, (result.TokensInput + result.TokensOutput) * 0.000003m);

        var extraction = ParseExtraction(result.Text);
        var nlRequest = BuildNlRequest(extraction, normalised);
        var generation = await _nlGenerator.GenerateAsync(nlRequest, workspaceId, cancellationToken);

        var process = new ExtractedProcess(
            extraction.Summary,
            extraction.Steps,
            extraction.Approvers,
            extraction.Systems,
            nlRequest);

        _log.LogInformation("ConversationImportService.ImportAsync exit confidence={Confidence}", extraction.Confidence);
        return new ConversationImportResult(generation.YamlContent, process, extraction.Confidence, result.TokensInput + result.TokensOutput + generation.TokensUsed);
    }

    private static string NormaliseConversation(string text, string sourceType)
    {
        if (sourceType.Equals("slack", StringComparison.OrdinalIgnoreCase))
            return ParseSlackJson(text);
        if (sourceType.Equals("teams", StringComparison.OrdinalIgnoreCase))
            return ParseTeamsTranscript(text);
        return text.Trim();
    }

    private static string ParseSlackJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return text;
            var lines = new System.Text.StringBuilder();
            foreach (var msg in doc.RootElement.EnumerateArray())
            {
                var user = msg.TryGetProperty("user", out var u) ? u.GetString() : "user";
                var content = msg.TryGetProperty("text", out var t) ? t.GetString() : "";
                if (!string.IsNullOrWhiteSpace(content))
                    lines.AppendLine($"{user}: {content}");
            }
            return lines.ToString();
        }
        catch
        {
            return text;
        }
    }

    private static string ParseTeamsTranscript(string text)
    {
        // Teams transcripts often have "Name (HH:MM)\nMessage" format
        return TeamsLineRegex().Replace(text, "$1: ").Trim();
    }

    [GeneratedRegex(@"^(.+?)\s+\(\d{1,2}:\d{2}\)\s*$", RegexOptions.Multiline)]
    private static partial Regex TeamsLineRegex();

    private static ExtractionDto ParseExtraction(string json)
    {
        try
        {
            var clean = json.Trim();
            if (clean.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) clean = clean[7..].Trim();
            else if (clean.StartsWith("```", StringComparison.Ordinal)) clean = clean[3..].Trim();
            if (clean.EndsWith("```", StringComparison.Ordinal)) clean = clean[..^3].Trim();
            return JsonSerializer.Deserialize<ExtractionDto>(clean, JsonOpts) ?? new ExtractionDto();
        }
        catch
        {
            return new ExtractionDto { Summary = json.Length > 200 ? json[..200] : json };
        }
    }

    private static NlWorkflowRequest BuildNlRequest(ExtractionDto e, string originalText)
    {
        var name = string.IsNullOrWhiteSpace(e.WorkflowName) ? "Imported Workflow" : e.WorkflowName;
        var steps = e.Steps.Count > 0 ? string.Join(". ", e.Steps) : "Process steps extracted from conversation";
        var approvers = e.Approvers.Count > 0 ? $"Approvers: {string.Join(", ", e.Approvers)}. " : "";
        var rules = string.IsNullOrWhiteSpace(e.Rules) ? "None" : e.Rules;

        return new NlWorkflowRequest(
            WorkflowName: name,
            Purpose: e.Summary,
            TriggerDescription: string.IsNullOrWhiteSpace(e.Trigger) ? "Manual or event-based trigger" : e.Trigger,
            StepsDescription: steps,
            RulesAndConstraints: $"{approvers}{rules}",
            SystemsAndAi: e.Systems.Count > 0 ? string.Join(", ", e.Systems) : "None specified",
            ExistingContext: $"Extracted from conversation. Original: {originalText.Substring(0, Math.Min(500, originalText.Length))}");
    }

    private sealed class ExtractionDto
    {
        [JsonPropertyName("workflow_name")] public string WorkflowName { get; set; } = "";
        [JsonPropertyName("summary")] public string Summary { get; set; } = "Imported workflow";
        [JsonPropertyName("steps")] public List<string> Steps { get; set; } = [];
        [JsonPropertyName("approvers")] public List<string> Approvers { get; set; } = [];
        [JsonPropertyName("systems")] public List<string> Systems { get; set; } = [];
        [JsonPropertyName("trigger")] public string Trigger { get; set; } = "";
        [JsonPropertyName("rules")] public string Rules { get; set; } = "";
        [JsonPropertyName("confidence")] public double Confidence { get; set; } = 0.8;
    }
}
