using System.Text;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Flowamaz.Application.Workflow.Creation;

public sealed class SopParsingService : ISopParsingService
{
    private readonly IModelResolutionService _modelResolution;
    private readonly IAiCompletionService _ai;
    private readonly IAiTokenMeteringService _metering;
    private readonly INlYamlGenerationService _nlGenerator;
    private readonly ILogger<SopParsingService> _log;

    private const string FunctionId = "F7";

    private const string ExtractionSystemPrompt =
        "You are a process analyst. Given a document extract, identify the process steps. " +
        "Return JSON with this structure:\n" +
        "{\n  \"workflow_name\": \"string\",\n  \"purpose\": \"string\",\n  \"trigger\": \"string\",\n" +
        "  \"steps\": [\"step 1\", \"step 2\"],\n  \"rules\": \"string\",\n  \"systems\": \"string\"\n}";

    public SopParsingService(
        IModelResolutionService modelResolution,
        IAiCompletionService ai,
        IAiTokenMeteringService metering,
        INlYamlGenerationService nlGenerator,
        ILogger<SopParsingService> log)
    {
        _modelResolution = modelResolution;
        _ai = ai;
        _metering = metering;
        _nlGenerator = nlGenerator;
        _log = log;
    }

    public async Task<SopParseResult> ParseAsync(
        byte[] fileBytes, string mimeType, Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        _log.LogInformation("SopParsingService.ParseAsync entry workspaceId={WorkspaceId} mimeType={MimeType} bytes={Bytes}",
            workspaceId, mimeType, fileBytes.Length);

        var (rawText, pageCount) = ExtractText(fileBytes, mimeType);
        var wordCount = CountWords(rawText);

        // Truncate to ~80k chars to stay within long-context window
        var truncated = rawText.Length > 80_000 ? rawText[..80_000] : rawText;

        var config = await _modelResolution.ResolveModelConfigAsync(FunctionId, workspaceId, cancellationToken);
        var userPrompt = $"Document text:\n\n{truncated}";
        var aiResult = await _ai.CompleteAsync(config, ExtractionSystemPrompt, userPrompt, cancellationToken);

        var costUsd = (aiResult.TokensInput + aiResult.TokensOutput) * 0.000000015m;
        _metering.RecordUsage(FunctionId, config.ModelId, config.Provider, null, workspaceId,
            aiResult.TokensInput, aiResult.TokensOutput, costUsd);

        var extracted = ParseExtractionJson(aiResult.Text);

        var genRequest = new NlWorkflowRequest(
            WorkflowName: extracted.WorkflowName,
            Purpose: extracted.Purpose,
            TriggerDescription: extracted.Trigger,
            StepsDescription: string.Join('\n', extracted.Steps.Select((s, i) => $"{i + 1}. {s}")),
            RulesAndConstraints: extracted.Rules,
            SystemsAndAi: extracted.Systems);

        var genResult = await _nlGenerator.GenerateAsync(genRequest, workspaceId, cancellationToken);

        var totalTokens = aiResult.TokensInput + aiResult.TokensOutput + genResult.TokensUsed;
        _log.LogInformation("SopParsingService.ParseAsync exit pages={Pages} words={Words} tokens={Tokens}",
            pageCount, wordCount, totalTokens);

        return new SopParseResult(genResult.YamlContent, extracted.Steps, pageCount, wordCount, totalTokens);
    }

    private static (string text, int pageCount) ExtractText(byte[] fileBytes, string mimeType)
    {
        if (mimeType == "application/pdf")
            return ExtractPdf(fileBytes);

        if (mimeType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document")
            return ExtractDocx(fileBytes);

        // Plain text
        return (Encoding.UTF8.GetString(fileBytes), 1);
    }

    private static (string text, int pageCount) ExtractPdf(byte[] fileBytes)
    {
        using var pdf = PdfDocument.Open(fileBytes);
        var sb = new StringBuilder();
        foreach (Page page in pdf.GetPages())
        {
            sb.AppendLine(page.Text);
        }
        return (sb.ToString(), pdf.NumberOfPages);
    }

    private static (string text, int pageCount) ExtractDocx(byte[] fileBytes)
    {
        using var ms = new MemoryStream(fileBytes);
        using var doc = WordprocessingDocument.Open(ms, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
            return (string.Empty, 1);

        var sb = new StringBuilder();
        foreach (var para in body.Descendants<Paragraph>())
        {
            sb.AppendLine(para.InnerText);
        }
        return (sb.ToString(), 1);
    }

    private static int CountWords(string text) =>
        text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;

    private static ExtractionDto ParseExtractionJson(string json)
    {
        var clean = json.Trim();
        // Strip markdown fences if present
        if (clean.StartsWith("```"))
        {
            clean = clean[(clean.IndexOf('\n') + 1)..];
            var fence = clean.LastIndexOf("```");
            if (fence > 0) clean = clean[..fence];
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(clean);
            var root = doc.RootElement;
            var steps = root.TryGetProperty("steps", out var stepsEl)
                ? stepsEl.EnumerateArray().Select(s => s.GetString() ?? string.Empty).ToList()
                : new List<string>();

            return new ExtractionDto(
                GetStr(root, "workflow_name", "Imported Workflow"),
                GetStr(root, "purpose", "Automated process"),
                GetStr(root, "trigger", "Manual trigger"),
                steps,
                GetStr(root, "rules", string.Empty),
                GetStr(root, "systems", string.Empty));
        }
        catch
        {
            return new ExtractionDto("Imported Workflow", "Automated process", "Manual trigger",
                new List<string> { "Review document and define steps" }, string.Empty, string.Empty);
        }
    }

    private static string GetStr(System.Text.Json.JsonElement el, string prop, string fallback) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;

    private sealed record ExtractionDto(
        string WorkflowName, string Purpose, string Trigger,
        IReadOnlyList<string> Steps, string Rules, string Systems);
}
