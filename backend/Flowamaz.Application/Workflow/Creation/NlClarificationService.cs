using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Creation;

/// <summary>
/// Clarifying questions before NL→YAML generation (FUNCTIONAL.md §6.1). Cost order:
/// <list type="number">
/// <item>Deterministic heuristics (zero cost) for the four essentials: trigger, approver, rejection path, systems.</item>
/// <item>Only when the heuristics find nothing <i>and</i> the steps are too thin to judge, one small AI call
/// (F2 model via ResolveModelConfig, budget-checked, semantic-cached, metered). Any AI failure falls back
/// to the heuristic result — clarification is optional and must never block generation.</item>
/// </list>
/// With the AI stub the AI response is not JSON, so the heuristics alone decide.
/// </summary>
public sealed class NlClarificationService : INlClarificationService
{
    public const int MaxQuestions = 3;
    private const int AiMaxTokens = 400;

    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "-", "--", "?", "??", "tbd", "tbc", "todo", "unknown", "not sure", "idk", "...",
    };

    private static readonly HashSet<string> ExplicitNone = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "no", "n/a", "na", "nil", "no systems", "none needed", "no ai", "nothing",
    };

    private static readonly Regex ApprovalWords = new(
        @"\b(approv\w*|sign[- ]?off|signs off|authori[sz]\w*|endorse\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ApproverIdentity = new(
        @"(\b(manager|managers|director|head|lead|supervisor|finance|accounts|accounting|hr|human resources|team|owner|admin|administrator|cfo|ceo|cto|coo|cio|vp|officer|department|committee|legal|compliance|board|controller|procurement|security|partner|principal|reviewer|customer|client)\b)|@\w+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RejectionWords = new(
        @"\b(reject\w*|declin\w*|den(y|ies|ied)|refus\w*|not approved|disapprov\w*|otherwise|else|send(s)? (it )?back|sent back|return(s|ed)? (it )?to|if not|if no)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TriggerWords = new(
        @"\b(when|whenever|upon|on (submit|receipt|creation)|submit\w*|form|webhook|schedul\w*|daily|weekly|monthly|hourly|every|cron|manual\w*|button|receiv\w*|arriv\w*|event|starts? (when|by|with)|trigger\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SystemWords = new(
        @"\b(slack|teams|email|e-mail|outlook|gmail|sap|salesforce|jira|servicenow|hubspot|zendesk|github|gitlab|aws|azure|google|sharepoint|excel|spreadsheet|sheet|database|db|sql|postgres|mysql|erp|crm|hris|bamboohr|workday|stripe|twilio|sms|api|webhook|docusign|dropbox|onedrive|notion|trello|asana)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StepSeparators = new(@"(\r?\n|;|\.\s+|\bthen\b|\d+[.)]\s)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private const string AiSystemPrompt = """
        You review short business process descriptions before they are turned into an automated workflow.
        List at most 3 questions whose answers are essential to build the workflow and are missing from the description:
        who approves, what starts it, what happens on rejection or failure, which systems are involved, or another critical gap.
        Return JSON ONLY: {"questions":[{"topic":"trigger|approver|rejection|systems|other","question":"..."}]}
        Return {"questions":[]} if nothing essential is missing.
        """;

    private readonly IAiCompletionService _ai;
    private readonly IModelResolutionService _models;
    private readonly IAiTokenMeteringService _metering;
    private readonly IAiBudgetService _budget;
    private readonly ISemanticCacheService _cache;
    private readonly ILogger<NlClarificationService> _log;

    public NlClarificationService(
        IAiCompletionService ai,
        IModelResolutionService models,
        IAiTokenMeteringService metering,
        IAiBudgetService budget,
        ISemanticCacheService cache,
        ILogger<NlClarificationService> log)
    {
        _ai = ai;
        _models = models;
        _metering = metering;
        _budget = budget;
        _cache = cache;
        _log = log;
    }

    public async Task<ClarificationResult> GetQuestionsAsync(NlWorkflowRequest request, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        _log.LogInformation("NlClarificationService.GetQuestionsAsync entry workspaceId={WorkspaceId}", workspaceId);
        try
        {
            var heuristic = DetectMissingEssentials(request);
            if (heuristic.Count > 0 || !IsTooThinToJudge(request))
            {
                _log.LogInformation("NlClarificationService.GetQuestionsAsync exit heuristic questions={Count}", heuristic.Count);
                return new ClarificationResult(heuristic, UsedAi: false, TokensUsed: 0);
            }

            var aiResult = await TryAiQuestionsAsync(request, workspaceId, cancellationToken);
            _log.LogInformation("NlClarificationService.GetQuestionsAsync exit ai questions={Count} tokens={Tokens}",
                aiResult.Questions.Count, aiResult.TokensUsed);
            return aiResult;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "NlClarificationService.GetQuestionsAsync error workspaceId={WorkspaceId}", workspaceId);
            throw;
        }
    }

    /// <summary>
    /// Zero-cost detection of the four essentials. Answers the user already gave (request.Clarifications)
    /// count as part of the description, so a re-check after answering does not ask again.
    /// </summary>
    public static IReadOnlyList<ClarifyingQuestion> DetectMissingEssentials(NlWorkflowRequest request)
    {
        var answered = string.Join("\n", (request.Clarifications ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Answer))
            .Select(c => c.Answer));
        var answeredTopics = (request.Clarifications ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Answer))
            .Select(c => c.Topic)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var all = string.Join("\n", request.Purpose, request.TriggerDescription, request.StepsDescription,
            request.RulesAndConstraints, request.SystemsAndAi, request.ExistingContext, answered);
        var questions = new List<ClarifyingQuestion>();

        // 1. Trigger — the trigger field is empty/placeholder and nothing else says what starts it.
        if (!answeredTopics.Contains(ClarificationTopics.Trigger)
            && IsBlank(request.TriggerDescription)
            && !TriggerWords.IsMatch(string.Join("\n", request.Purpose, request.StepsDescription, answered)))
        {
            questions.Add(new ClarifyingQuestion("q-trigger", ClarificationTopics.Trigger,
                "What starts this workflow?",
                "e.g. 'An employee submits the purchase form', 'Every Monday at 9am', or 'A webhook from our CRM'."));
        }

        var mentionsApproval = ApprovalWords.IsMatch(all);

        // 2. Approver — approval is mentioned but no person, role or team is named.
        if (mentionsApproval && !answeredTopics.Contains(ClarificationTopics.Approver) && !NamesApprover(all))
        {
            questions.Add(new ClarifyingQuestion("q-approver", ClarificationTopics.Approver,
                "Who approves this request?",
                "Name a role, team or person, e.g. 'The requester's line manager' or 'Finance team'."));
        }

        // 3. Rejection — approval is mentioned but there is no rejection / otherwise path.
        if (mentionsApproval && !answeredTopics.Contains(ClarificationTopics.Rejection) && !RejectionWords.IsMatch(all))
        {
            questions.Add(new ClarifyingQuestion("q-rejection", ClarificationTopics.Rejection,
                "What should happen if the request is rejected?",
                "e.g. 'Notify the requester with the reason and close it' or 'Send it back for changes'."));
        }

        // 4. Systems — the systems field is empty and no system is mentioned anywhere.
        if (!answeredTopics.Contains(ClarificationTopics.Systems)
            && IsBlank(request.SystemsAndAi)
            && !SystemWords.IsMatch(all))
        {
            questions.Add(new ClarifyingQuestion("q-systems", ClarificationTopics.Systems,
                "Which systems or apps does this workflow use?",
                "e.g. 'Slack for notifications, SAP for purchase orders'. Answer 'none' if it is purely manual."));
        }

        return questions.Take(MaxQuestions).ToList();
    }

    /// <summary>Steps with fewer than two identifiable steps cannot be judged by keyword heuristics.</summary>
    public static bool IsTooThinToJudge(NlWorkflowRequest request)
    {
        var steps = StepSeparators.Split(request.StepsDescription ?? string.Empty)
            .Count(s => s.Trim().Length >= 8);
        return steps < 2;
    }

    private static bool IsBlank(string? value)
    {
        var v = (value ?? string.Empty).Trim().TrimEnd('.');
        if (ExplicitNone.Contains(v)) return false; // the user explicitly said "none" — that is an answer
        return Placeholders.Contains(v) || v.Length < 4;
    }

    /// <summary>
    /// True when a sentence that mentions approval also names a role/team/person, or an explicit
    /// approver was given ("Priya approves", "approved by Ahmad"). Checking per sentence stops a role named
    /// elsewhere ("finance pays the invoice") from being mistaken for the approver.
    /// </summary>
    private static bool NamesApprover(string text)
    {
        var sentences = Regex.Split(text, @"(\r?\n|[.;!?](\s|$))");
        foreach (var sentence in sentences.Where(s => ApprovalWords.IsMatch(s)))
        {
            if (ApproverIdentity.IsMatch(sentence)) return true;
            if (Regex.IsMatch(sentence, @"\b[A-Z][a-z]+ (approves|signs off|authori[sz]es)\b|\b(approved|signed off|authori[sz]ed) by [A-Z][a-z]+"))
                return true;
        }
        return false;
    }

    private async Task<ClarificationResult> TryAiQuestionsAsync(NlWorkflowRequest request, Guid workspaceId, CancellationToken ct)
    {
        _log.LogInformation("NlClarificationService.TryAiQuestionsAsync entry workspaceId={WorkspaceId}", workspaceId);
        var empty = new ClarificationResult([], UsedAi: false, TokensUsed: 0);
        try
        {
            var userPrompt = BuildAiPrompt(request);
            var cacheKey = _cache.ComputeKey(AiFunctionIds.NlYaml, workspaceId, "clarify|" + userPrompt.Trim().ToLowerInvariant());
            var cached = await _cache.GetAsync(cacheKey, ct);
            if (cached is not null)
            {
                var fromCache = ParseAiQuestions(cached);
                _log.LogInformation("NlClarificationService.TryAiQuestionsAsync exit cache_hit questions={Count}", fromCache.Count);
                return new ClarificationResult(fromCache, UsedAi: true, TokensUsed: 0);
            }

            if (!await _budget.IsBudgetAvailableAsync(workspaceId, ct))
            {
                _log.LogInformation("NlClarificationService.TryAiQuestionsAsync exit skipped — AI budget exhausted");
                return empty;
            }

            var model = await _models.ResolveModelConfigAsync(AiFunctionIds.NlYaml, workspaceId, ct);
            var result = await _ai.CompleteAsync(model, AiSystemPrompt, userPrompt, ct, AiMaxTokens);
            var tokens = result.TokensInput + result.TokensOutput;
            _metering.RecordUsage(AiFunctionIds.NlYaml, model.ModelId, model.Provider, null, workspaceId,
                result.TokensInput, result.TokensOutput, tokens * 0.000003m);

            var questions = ParseAiQuestions(result.Text);
            if (questions.Count > 0 || LooksLikeJson(result.Text))
                await _cache.SetAsync(cacheKey, result.Text, TimeSpan.FromHours(24), ct);

            _log.LogInformation("NlClarificationService.TryAiQuestionsAsync exit questions={Count} tokens={Tokens}", questions.Count, tokens);
            return new ClarificationResult(questions, UsedAi: true, TokensUsed: tokens);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Optional step: AI not configured / provider down / plan limit must not block generation.
            var reason = ex is AppException app ? app.ErrorCode : ex.GetType().Name;
            _log.LogWarning(ex, "NlClarificationService.TryAiQuestionsAsync failed ({Reason}) — continuing without AI questions", reason);
            return empty;
        }
    }

    internal static IReadOnlyList<ClarifyingQuestion> ParseAiQuestions(string text)
    {
        var clean = (text ?? string.Empty).Trim();
        if (clean.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) clean = clean[7..].Trim();
        else if (clean.StartsWith("```", StringComparison.Ordinal)) clean = clean[3..].Trim();
        if (clean.EndsWith("```", StringComparison.Ordinal)) clean = clean[..^3].Trim();
        if (!LooksLikeJson(clean)) return [];

        try
        {
            var dto = JsonSerializer.Deserialize<AiQuestionsDto>(clean, JsonOpts);
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ClarificationTopics.Trigger, ClarificationTopics.Approver, ClarificationTopics.Rejection,
                ClarificationTopics.Systems, ClarificationTopics.Other,
            };
            return (dto?.Questions ?? [])
                .Where(q => !string.IsNullOrWhiteSpace(q.Question) && q.Question.Length <= 300)
                .Take(MaxQuestions)
                .Select((q, i) => new ClarifyingQuestion(
                    $"q-ai-{i + 1}",
                    known.Contains(q.Topic ?? string.Empty) ? q.Topic!.ToLowerInvariant() : ClarificationTopics.Other,
                    q.Question!.Trim()))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool LooksLikeJson(string text) => text.TrimStart().StartsWith('{');

    private static string BuildAiPrompt(NlWorkflowRequest r) =>
        $"Name: {r.WorkflowName}\nPurpose: {r.Purpose}\nTrigger: {r.TriggerDescription}\nSteps: {r.StepsDescription}\n" +
        $"Rules: {r.RulesAndConstraints}\nSystems: {r.SystemsAndAi}";

    private sealed class AiQuestionsDto
    {
        [JsonPropertyName("questions")] public List<AiQuestionDto> Questions { get; set; } = [];
    }

    private sealed class AiQuestionDto
    {
        [JsonPropertyName("topic")] public string? Topic { get; set; }
        [JsonPropertyName("question")] public string? Question { get; set; }
    }
}
