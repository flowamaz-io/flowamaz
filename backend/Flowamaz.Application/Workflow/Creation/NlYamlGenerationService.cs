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

    private const string SystemPrompt = """
        You are an expert workflow automation engineer. Your task is to generate a valid Flowamaz workflow YAML from a structured description.

        RULES:
        - Output ONLY the YAML. No explanation, no markdown fences, no commentary.
        - Use only declared variable names in ${variable} references.
        - Every workflow MUST have exactly one trigger node.
        - Every workflow MUST have exactly one end node.
        - Use only these node types: trigger, action, ai, human-gate, router, end, foreach, parallel, try-catch, wait, sub-workflow, annotation.
        - If you cannot model a step precisely, add an annotation node with a clear note.
        - All IDs must be kebab-case, unique, and descriptive.

        SCHEMA SUMMARY:
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: <kebab-case-id>
          name: "<human name>"
          description: "<description>"
          version: "1.0.0"
        spec:
          trigger:
            type: <manual|webhook|schedule|event>
          sla_threshold_ms: <optional ms>
          variables:
            <name>:
              type: <string|number|boolean|object>
              required: <true|false>
              sensitive: <true|false>
          nodes:
            - id: <kebab-id>
              type: <node-type>
              label: "<label>"
              config: {}
              retry:
                max_attempts: 3
                backoff_seconds: 5
                backoff_multiplier: 2
              timeout:
                seconds: 30
              compensate:
                strategy: <backward|forward|pivot>
          edges:
            - id: <edge-id>
              from: <source-node-id>
              to: <target-node-id>
              via: <optional condition expression>

        EXAMPLE 1 — Purchase Approval:
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: purchase-approval
          name: "Purchase Approval"
          description: "Routes purchase requests for manager approval"
          version: "1.0.0"
        spec:
          trigger:
            type: webhook
          sla_threshold_ms: 172800000
          variables:
            amount:
              type: number
              required: true
              sensitive: false
            requester:
              type: string
              required: true
              sensitive: false
          nodes:
            - id: start
              type: trigger
              label: "Purchase Request Received"
              config: {}
            - id: validate-request
              type: action
              label: "Validate Request"
              config: {}
            - id: amount-router
              type: router
              label: "Amount Router"
              config: {}
            - id: manager-approval
              type: human-gate
              label: "Manager Approval"
              config: {}
              timeout:
                seconds: 172800
                on_timeout: reject
            - id: auto-approve
              type: action
              label: "Auto Approve"
              config: {}
            - id: reject
              type: action
              label: "Reject Request"
              config: {}
            - id: end
              type: end
              label: "Done"
              config: {}
          edges:
            - id: e1
              from: start
              to: validate-request
            - id: e2
              from: validate-request
              to: amount-router
            - id: e3
              from: amount-router
              to: manager-approval
              via: "${amount} > 1000"
            - id: e4
              from: amount-router
              to: auto-approve
              via: "${amount} <= 1000"
            - id: e5
              from: manager-approval
              to: end
            - id: e6
              from: auto-approve
              to: end
            - id: e7
              from: reject
              to: end

        EXAMPLE 2 — Employee Onboarding:
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: employee-onboarding
          name: "Employee Onboarding"
          description: "Automates new hire setup across HR, IT, and Finance"
          version: "1.0.0"
        spec:
          trigger:
            type: event
          variables:
            employee_id:
              type: string
              required: true
              sensitive: false
            department:
              type: string
              required: true
              sensitive: false
          nodes:
            - id: start
              type: trigger
              label: "New Hire Created"
              config: {}
            - id: setup-accounts
              type: parallel
              label: "Setup Accounts"
              config: {}
            - id: send-welcome-email
              type: action
              label: "Send Welcome Email"
              config: {}
            - id: provision-hardware
              type: action
              label: "Provision Hardware"
              config: {}
            - id: hr-orientation
              type: human-gate
              label: "HR Orientation Sign-off"
              config: {}
              timeout:
                seconds: 86400
            - id: end
              type: end
              label: "Onboarding Complete"
              config: {}
          edges:
            - id: e1
              from: start
              to: setup-accounts
            - id: e2
              from: setup-accounts
              to: send-welcome-email
            - id: e3
              from: setup-accounts
              to: provision-hardware
            - id: e4
              from: send-welcome-email
              to: hr-orientation
            - id: e5
              from: provision-hardware
              to: hr-orientation
            - id: e6
              from: hr-orientation
              to: end

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

        // FIX 3: Complexity routing — simple workflows use Haiku (~80% cheaper), complex use Sonnet
        var modelConfig = await _models.ResolveModelConfigAsync(AiFunctionIds.NlYaml, workspaceId, cancellationToken);
        var complexity = ComplexityScore(request);
        if (complexity <= 5)
            modelConfig = modelConfig with { ModelId = "claude-haiku-4-5" };

        // FIX 6: Cap output tokens based on estimated workflow size (nodes * 200, max 4096)
        var maxTokens = Math.Min(4096, estimatedNodes * 200);

        var userPrompt = BuildUserPrompt(request);

        _log.LogInformation("NlYamlGenerationService.GenerateAsync calling AI model={ModelId} complexity={Complexity} maxTokens={MaxTokens}",
            modelConfig.ModelId, complexity, maxTokens);

        var result = await _ai.CompleteAsync(modelConfig, SystemPrompt, userPrompt, cancellationToken, maxTokens);

        var yaml = ExtractYaml(result.Text);
        var validation = await _validator.ValidateAsync(yaml, workspaceId, cancellationToken);

        // One self-correction attempt on validation failure
        if (!validation.IsValid)
        {
            _log.LogWarning("NlYamlGenerationService.GenerateAsync validation_failed errors={Count} attempting self-correction", validation.Errors.Count);
            var correctionPrompt = BuildCorrectionPrompt(userPrompt, yaml, validation);
            var corrected = await _ai.CompleteAsync(modelConfig, SystemPrompt, correctionPrompt, cancellationToken, maxTokens);
            var correctedYaml = ExtractYaml(corrected.Text);
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
        return $"{N(r.WorkflowName)}|{N(r.Purpose)}|{N(r.TriggerDescription)}|{N(r.StepsDescription)}|{N(r.RulesAndConstraints)}|{N(r.SystemsAndAi)}|{N(r.ExistingContext)}";
    }

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
        sb.AppendLine();
        sb.AppendLine("Output ONLY the YAML. No explanation.");
        return sb.ToString();
    }

    private static string BuildCorrectionPrompt(string originalPrompt, string invalidYaml, Core.Workflow.ValidationResult validation)
    {
        var errors = string.Join("\n", validation.Errors.Select(e => $"- [{e.Code}] {e.Message}"));
        return $"{originalPrompt}\n\nYour previous response had validation errors:\n{errors}\n\nCorrected YAML:\n\n";
    }

    private static string ExtractYaml(string text)
    {
        // Strip markdown code fences if present
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
