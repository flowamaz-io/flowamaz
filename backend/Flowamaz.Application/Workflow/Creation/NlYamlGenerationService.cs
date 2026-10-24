using FluentValidation;
using FluentValidation.Results;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Creation;

public sealed class NlYamlGenerationService : INlYamlGenerationService
{
    private readonly IAiCompletionService _ai;
    private readonly IModelResolutionService _models;
    private readonly ISemanticCacheService _cache;
    private readonly IAiTokenMeteringService _metering;
    private readonly IWorkflowValidator _validator;
    private readonly IAiBudgetService _budget;
    private readonly ILogger<NlYamlGenerationService> _log;

    // Every rule and example below must match workflow-v1.schema.json and the engine. The prompt used to
    // teach keys the validator rejects (trigger type "event", "${var}" references, node-level gate timeouts),
    // so live generation failed. NlYamlPromptTests validates each example in this prompt.
    internal const string SystemPrompt = """
        You are an expert workflow automation engineer. Generate a valid Flowamaz workflow definition (YAML)
        from a structured description.

        OUTPUT
        - Output ONLY the YAML document. No explanation, no markdown fences, no commentary.

        DOCUMENT SHAPE (no other top-level keys)
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: <kebab-case-id>
          name: "<human name>"
          description: "<one sentence>"
          version: "1.0.0"
        spec:
          trigger:
            type: <form|webhook|schedule|manual>
          variables:
            <snake_case_name>:
              type: <string|number|boolean|object|array>
              required: <true|false>
          nodes: [...]
          edges: [...]

        TRIGGER TYPE — pick exactly one of these values (anything else is invalid):
        - form: a person submits a request or form (expense claim, leave request, application).
        - webhook: another system sends an event (new order, new hire created in HR system).
        - schedule: runs on a timetable (daily report, monthly close).
        - manual: someone starts it by hand.

        NODES — each node has ONLY these keys: id, type, label, config. Nothing else at node level.
        - id: lowercase letters, numbers and hyphens only, unique (e.g. manager-approval).
        - type: trigger | action | ai | human-gate | router | wait | end | annotation
        - Exactly one node of type trigger (first node). At least one node of type end.
        - action: a system step (send email, create record, update ERP). Use config: {}.
        - ai: an AI step (classify, summarise, extract). config: { prompt: "<instruction using {{ variable }}>" }
        - human-gate: a person approves or rejects. config:
            delivery_channel: Portal
            gate_label: "<what the approver is asked>"
            timeout_hours: <whole number>
            on_timeout: <fail|escalate|auto-approve|auto-reject>
        - router: a decision. Two kinds:
            a) After a human-gate, a router with NO config whose outgoing edges use via: approved and via: rejected.
            b) A rule on data: config.branches lists one entry per exit:
                 config:
                   branches:
                     - { label: "Over 1000", condition: "{{ amount }} > 1000" }
                     - { label: "Up to 1000", condition: "{{ amount }} <= 1000" }
               Each branch needs exactly one outgoing edge whose via equals the branch label.
        - annotation: a note for anything you cannot model precisely. Add content: "<note>" (annotation only).

        VARIABLES
        - Declare every input the steps use under spec.variables. Reference them with double curly braces: {{ name }}.

        EDGES — each edge has ONLY: id, from, to, and optionally via.
        - Every node except end nodes needs at least one outgoing edge; every node must be reachable from the trigger.
        - A router always has at least two outgoing edges (one per decision outcome or branch).
        - Rejections usually lead to a notification action and then an end node.

        EXAMPLE 1 — Purchase Approval:
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: purchase-approval
          name: "Purchase Approval"
          description: "Manager approves every purchase request; Finance also approves requests over 1000."
          version: "1.0.0"
        spec:
          trigger:
            type: form
          variables:
            amount:
              type: number
              required: true
            requester_email:
              type: string
              required: true
            item:
              type: string
              required: true
          nodes:
            - id: request-submitted
              type: trigger
              label: "Purchase request submitted"
            - id: manager-approval
              type: human-gate
              label: "Manager approval"
              config:
                delivery_channel: Portal
                gate_label: "Approve purchase of {{ item }} for {{ amount }}"
                timeout_hours: 48
                on_timeout: escalate
            - id: manager-decision
              type: router
              label: "Manager approved?"
            - id: amount-check
              type: router
              label: "Over 1000?"
              config:
                branches:
                  - { label: "Over 1000", condition: "{{ amount }} > 1000" }
                  - { label: "Up to 1000", condition: "{{ amount }} <= 1000" }
            - id: finance-approval
              type: human-gate
              label: "Finance approval"
              config:
                delivery_channel: Portal
                gate_label: "Finance sign-off for {{ amount }}"
                timeout_hours: 24
                on_timeout: fail
            - id: finance-decision
              type: router
              label: "Finance approved?"
            - id: create-purchase-order
              type: action
              label: "Create purchase order"
              config: {}
            - id: notify-rejected
              type: action
              label: "Notify requester of rejection"
              config: {}
            - id: done
              type: end
              label: "Purchase order created"
            - id: rejected
              type: end
              label: "Request rejected"
          edges:
            - { id: e1, from: request-submitted, to: manager-approval }
            - { id: e2, from: manager-approval, to: manager-decision }
            - { id: e3, from: manager-decision, to: amount-check, via: approved }
            - { id: e4, from: manager-decision, to: notify-rejected, via: rejected }
            - { id: e5, from: amount-check, to: finance-approval, via: "Over 1000" }
            - { id: e6, from: amount-check, to: create-purchase-order, via: "Up to 1000" }
            - { id: e7, from: finance-approval, to: finance-decision }
            - { id: e8, from: finance-decision, to: create-purchase-order, via: approved }
            - { id: e9, from: finance-decision, to: notify-rejected, via: rejected }
            - { id: e10, from: create-purchase-order, to: done }
            - { id: e11, from: notify-rejected, to: rejected }

        EXAMPLE 2 — Employee Onboarding:
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: employee-onboarding
          name: "Employee Onboarding"
          description: "Sets up a new hire's accounts and hardware, then HR confirms orientation."
          version: "1.0.0"
        spec:
          trigger:
            type: webhook
          variables:
            employee_name:
              type: string
              required: true
            department:
              type: string
              required: true
          nodes:
            - id: new-hire-created
              type: trigger
              label: "New hire created in HR system"
            - id: create-accounts
              type: action
              label: "Create email and system accounts"
              config: {}
            - id: send-welcome-email
              type: action
              label: "Send welcome email"
              config: {}
            - id: provision-hardware
              type: action
              label: "Order laptop for {{ department }}"
              config: {}
            - id: hr-orientation
              type: human-gate
              label: "HR confirms orientation done"
              config:
                delivery_channel: Portal
                gate_label: "Confirm {{ employee_name }} completed orientation"
                timeout_hours: 120
                on_timeout: escalate
            - id: onboarding-complete
              type: end
              label: "Onboarding complete"
          edges:
            - { id: e1, from: new-hire-created, to: create-accounts }
            - { id: e2, from: create-accounts, to: send-welcome-email }
            - { id: e3, from: send-welcome-email, to: provision-hardware }
            - { id: e4, from: provision-hardware, to: hr-orientation }
            - { id: e5, from: hr-orientation, to: onboarding-complete }

        Now generate a complete workflow YAML from the structured description provided.
        """;

    public NlYamlGenerationService(
        IAiCompletionService ai,
        IModelResolutionService models,
        ISemanticCacheService cache,
        IAiTokenMeteringService metering,
        IWorkflowValidator validator,
        IAiBudgetService budget,
        ILogger<NlYamlGenerationService> log)
    {
        _ai = ai;
        _models = models;
        _cache = cache;
        _metering = metering;
        _validator = validator;
        _budget = budget;
        _log = log;
    }

    public async Task<GenerationResult> GenerateAsync(NlWorkflowRequest request, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        _log.LogInformation("NlYamlGenerationService.GenerateAsync entry workspaceId={WorkspaceId} workflow={Name}", workspaceId, request.WorkflowName);

        // FIX 4: Pre-screen input — zero AI cost for invalid inputs
        ValidateInput(request);
        var estimatedNodes = request.StepsDescription.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length + 2;

        // FIX 1: Cache check with per-field normalised key — identical requests from any user return cached result
        var cacheKey = _cache.ComputeKey(AiFunctionIds.NlYaml, workspaceId, BuildCacheInput(request));
        var cached = await _cache.GetAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            _log.LogInformation("NlYamlGenerationService.GenerateAsync cache_hit workspaceId={WorkspaceId}", workspaceId);
            var cachedValidation = await _validator.ValidateAsync(cached, workspaceId, cancellationToken);
            return new GenerationResult(cached, cachedValidation, 0, Cached: true);
        }

        // FIX 5: Budget check — only gate actual AI calls, not cache hits
        if (!await _budget.IsBudgetAvailableAsync(workspaceId, cancellationToken))
            throw new PlanLimitException("workflow generation");

        // O7: the model is exactly what ResolveModelConfig returns for F2 (the one Workspace settings show).
        // A hardcoded "simple workflows use Haiku" swap used to override it silently (and broke CLAUDE.md
        // rule 4). To use a cheaper model for F2, set it in Settings → Workspace → AI model configuration.
        var modelConfig = await _models.ResolveModelConfigAsync(AiFunctionIds.NlYaml, workspaceId, cancellationToken);
        AiNotConfiguredException.ThrowIfNoKey(modelConfig, "Generating a workflow from plain English");
        var complexity = ComplexityScore(request);

        // Output budget. "nodes × 200" gave a one-line description 600 tokens, which cut the YAML off
        // mid-document (a 10-node workflow is ~2,000 tokens). Tokens are billed as used, not as allowed.
        var maxTokens = Math.Clamp(estimatedNodes * 400, 4096, 8192);

        var userPrompt = BuildUserPrompt(request);

        _log.LogInformation("NlYamlGenerationService.GenerateAsync calling AI model={ModelId} complexity={Complexity} maxTokens={MaxTokens}",
            modelConfig.ModelId, complexity, maxTokens);

        var result = await _ai.CompleteAsync(modelConfig, SystemPrompt, userPrompt, cancellationToken, maxTokens);

        var yaml = GeneratedYamlRepair.Repair(ExtractYaml(result.Text));
        var validation = await _validator.ValidateAsync(yaml, workspaceId, cancellationToken);

        // One self-correction attempt on validation failure
        if (!validation.IsValid)
        {
            _log.LogWarning("NlYamlGenerationService.GenerateAsync validation_failed errors={Count} attempting self-correction", validation.Errors.Count);
            var correctionPrompt = BuildCorrectionPrompt(userPrompt, yaml, validation);
            var corrected = await _ai.CompleteAsync(modelConfig, SystemPrompt, correctionPrompt, cancellationToken, maxTokens);
            var correctedYaml = GeneratedYamlRepair.Repair(ExtractYaml(corrected.Text));
            var correctedValidation = await _validator.ValidateAsync(correctedYaml, workspaceId, cancellationToken);

            var totalTokens = result.TokensInput + result.TokensOutput + corrected.TokensInput + corrected.TokensOutput;
            _metering.RecordUsage(AiFunctionIds.NlYaml, modelConfig.ModelId, modelConfig.Provider,
                null, workspaceId, result.TokensInput + corrected.TokensInput,
                result.TokensOutput + corrected.TokensOutput, EstimateCost(totalTokens));

            if (correctedValidation.IsValid)
                await _cache.SetAsync(cacheKey, correctedYaml, TimeSpan.FromHours(24), cancellationToken);

            _log.LogInformation("NlYamlGenerationService.GenerateAsync exit corrected isValid={IsValid}", correctedValidation.IsValid);
            return new GenerationResult(correctedYaml, correctedValidation, totalTokens, Cached: false);
        }

        var tokens = result.TokensInput + result.TokensOutput;
        _metering.RecordUsage(AiFunctionIds.NlYaml, modelConfig.ModelId, modelConfig.Provider,
            null, workspaceId, result.TokensInput, result.TokensOutput, EstimateCost(tokens));

        await _cache.SetAsync(cacheKey, yaml, TimeSpan.FromHours(24), cancellationToken);

        _log.LogInformation("NlYamlGenerationService.GenerateAsync exit isValid={IsValid} tokens={Tokens}", validation.IsValid, tokens);
        return new GenerationResult(yaml, validation, tokens, Cached: false);
    }

    // FIX 4: Inline input pre-screening — throws ValidationException for actionable errors (zero AI cost for bad input)
    private static void ValidateInput(NlWorkflowRequest request)
    {
        var failures = new List<ValidationFailure>();

        if (request.WorkflowName.Trim().Length < 3)
            failures.Add(new ValidationFailure(nameof(request.WorkflowName),
                "Workflow name is too short. Enter a meaningful name (at least 3 characters)."));

        if (request.StepsDescription.Trim().Length < 30)
            failures.Add(new ValidationFailure(nameof(request.StepsDescription),
                "Steps description is too brief. Please describe at least 2-3 process steps in detail."));

        var estimatedNodes = request.StepsDescription.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length + 2;
        if (estimatedNodes > 25)
            failures.Add(new ValidationFailure(nameof(request.StepsDescription),
                "This workflow is very complex. Consider breaking it into smaller sub-workflows for better maintainability."));

        if (failures.Count > 0)
            throw new ValidationException(failures);
    }

    // FIX 3: Score determines model: ≤5 → Haiku (simple, cheap), >5 → Sonnet (complex, quality)
    public static int ComplexityScore(NlWorkflowRequest request)
    {
        var score = request.StepsDescription.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        if (request.StepsDescription.Contains("if", StringComparison.OrdinalIgnoreCase)) score += 2;
        if (request.StepsDescription.Contains("approval", StringComparison.OrdinalIgnoreCase)) score += 2;
        if (request.StepsDescription.Contains("parallel", StringComparison.OrdinalIgnoreCase)) score += 3;
        return score;
    }

    // FIX 1: Per-field normalisation so "My Workflow" and "my workflow  " hash to the same cache key
    private static string BuildCacheInput(NlWorkflowRequest r)
    {
        static string N(string? s) => (s ?? string.Empty).Trim().ToLowerInvariant();
        var key = $"{N(r.WorkflowName)}|{N(r.Purpose)}|{N(r.TriggerDescription)}|{N(r.StepsDescription)}|{N(r.RulesAndConstraints)}|{N(r.SystemsAndAi)}|{N(r.ExistingContext)}";
        var answers = AnsweredClarifications(r);
        // Unanswered requests keep the pre-clarification key so existing cache entries stay valid.
        return answers.Count == 0
            ? key
            : key + "|" + string.Join("|", answers.Select(a => $"{N(a.Topic)}={N(a.Answer)}"));
    }

    /// <summary>Clarification answers the user actually gave (skipped/blank answers are ignored).</summary>
    internal static IReadOnlyList<ClarificationAnswer> AnsweredClarifications(NlWorkflowRequest r) =>
        (r.Clarifications ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a.Answer))
            .ToList();

    private static string BuildUserPrompt(NlWorkflowRequest r)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Generate a Flowamaz workflow YAML from the following description:");
        sb.AppendLine();
        sb.AppendLine($"**Workflow Name:** {r.WorkflowName}");
        sb.AppendLine();
        sb.AppendLine($"**Purpose:** {r.Purpose}");
        sb.AppendLine();
        sb.AppendLine($"**Trigger:** {r.TriggerDescription}");
        sb.AppendLine();
        sb.AppendLine($"**Steps:** {r.StepsDescription}");
        sb.AppendLine();
        sb.AppendLine($"**Rules and Constraints:** {r.RulesAndConstraints}");
        sb.AppendLine();
        sb.AppendLine($"**Systems and AI:** {r.SystemsAndAi}");
        if (!string.IsNullOrWhiteSpace(r.ExistingContext))
        {
            sb.AppendLine();
            sb.AppendLine($"**Existing Context:** {r.ExistingContext}");
        }
        var answers = AnsweredClarifications(r);
        if (answers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Clarifications (answers from the user — treat as authoritative):**");
            foreach (var a in answers)
                sb.AppendLine($"- Q: {(a.Question ?? string.Empty).Trim()} A: {a.Answer.Trim()}");
        }
        sb.AppendLine();
        sb.AppendLine("Output ONLY the YAML. No explanation.");
        return sb.ToString();
    }

    private static string BuildCorrectionPrompt(string originalPrompt, string invalidYaml, Core.Workflow.ValidationResult validation)
    {
        // The model needs its own output to fix it; without it the "correction" was a fresh, equally wrong attempt.
        var errors = string.Join("\n", validation.Errors.Select(e => $"- [{e.Code}] {e.Message}"));
        return $"{originalPrompt}\n\nYour previous YAML was:\n{invalidYaml}\n\nIt failed validation:\n{errors}\n\n" +
               "Return the complete corrected YAML only, following every rule in the instructions.";
    }

    internal static string ExtractYaml(string text)
    {
        // A fenced block anywhere wins (models sometimes add a sentence before it); otherwise start at apiVersion.
        var fence = System.Text.RegularExpressions.Regex.Match(text, "```(?:ya?ml)?\\s*\\n(.*?)(?:```|$)",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (fence.Success && fence.Groups[1].Value.Contains("apiVersion", StringComparison.Ordinal))
            return fence.Groups[1].Value.Trim();
        var at = text.IndexOf("apiVersion:", StringComparison.Ordinal);
        if (at > 0) text = text[at..];

        var trimmed = text.Trim();
        if (trimmed.StartsWith("```yaml", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[7..].Trim();
        else if (trimmed.StartsWith("```", StringComparison.Ordinal))
            trimmed = trimmed[3..].Trim();
        if (trimmed.EndsWith("```", StringComparison.Ordinal))
            trimmed = trimmed[..^3].Trim();
        return trimmed;
    }

    private static decimal EstimateCost(int tokens) => tokens * 0.000003m;
}
