using Flowamaz.Core.Constants;
using Flowamaz.Core.Entities.Ai;
using Flowamaz.Core.Enums;

namespace Flowamaz.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotent seed data for the AI catalogue and platform AI config. Values come from
/// FUNCTIONAL.md §5.1 and §5.2. Applied via <c>HasData</c> in OnModelCreating so EF Core
/// embeds it into the migration as INSERTs — the migration is the seed.
/// </summary>
internal static class AiSeedData
{
    // Fixed seed timestamp so migrations are deterministic across machines.
    private static readonly DateTime SeedTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly List<string> AllPlans =
        ["community", "starter", "pro", "enterprise"];

    private static readonly List<string> ManagedPlans =
        ["starter", "pro", "enterprise"];

    public static IReadOnlyList<ModelCatalogue> Models { get; } =
    [
        // Anthropic — the only platform-managed provider (§5.1). ProviderModelId == ModelId (no dots).
        Model("claude-haiku-4-5", "claude-haiku-4-5", AiProviders.Anthropic, "Claude Haiku 4.5",
              hasVision: false, ctx: 200_000, jsonMode: true, streaming: true, plans: AllPlans),
        Model("claude-sonnet-4-6", "claude-sonnet-4-6", AiProviders.Anthropic, "Claude Sonnet 4.6",
              hasVision: true,  ctx: 200_000, jsonMode: true, streaming: true, plans: AllPlans),
        Model("claude-opus-4-6", "claude-opus-4-6", AiProviders.Anthropic, "Claude Opus 4.6",
              hasVision: true,  ctx: 200_000, jsonMode: true, streaming: true, plans: AllPlans),

        // Azure OpenAI — BYOK only (§5.1).
        Model("gpt-4o", "gpt-4o", AiProviders.AzureOpenAi, "GPT-4o",
              hasVision: true,  ctx: 128_000, jsonMode: true, streaming: true, plans: ManagedPlans),
        Model("gpt-4o-mini", "gpt-4o-mini", AiProviders.AzureOpenAi, "GPT-4o mini",
              hasVision: false, ctx: 128_000, jsonMode: true, streaming: true, plans: ManagedPlans),
        Model("o1-mini", "o1-mini", AiProviders.AzureOpenAi, "o1-mini",
              hasVision: false, ctx: 128_000, jsonMode: false, streaming: false, plans: ManagedPlans),

        // Google Vertex AI — BYOK only (§5.1). ProviderModelId keeps the dotted provider name.
        Model("gemini-2-0-flash", "gemini-2.0-flash", AiProviders.Google, "Gemini 2.0 Flash",
              hasVision: true,  ctx: 1_000_000, jsonMode: true, streaming: true, plans: ManagedPlans),
        Model("gemini-1-5-pro", "gemini-1.5-pro", AiProviders.Google, "Gemini 1.5 Pro",
              hasVision: true,  ctx: 1_000_000, jsonMode: true, streaming: true, plans: ManagedPlans),
        Model("gemini-1-5-flash", "gemini-1.5-flash", AiProviders.Google, "Gemini 1.5 Flash",
              hasVision: true,  ctx: 1_000_000, jsonMode: true, streaming: true, plans: ManagedPlans),

        // Kimi (Moonshot AI) — BYOK only (§5.1).
        Model("moonshot-v1-128k", "moonshot-v1-128k", AiProviders.Kimi, "Moonshot v1 128k",
              hasVision: false, ctx: 128_000, jsonMode: true, streaming: false, plans: ManagedPlans),
        Model("moonshot-v1-32k", "moonshot-v1-32k", AiProviders.Kimi, "Moonshot v1 32k",
              hasVision: false, ctx: 32_000, jsonMode: true, streaming: false, plans: ManagedPlans),
        Model("moonshot-v1-8k", "moonshot-v1-8k", AiProviders.Kimi, "Moonshot v1 8k",
              hasVision: false, ctx: 8_000, jsonMode: true, streaming: false, plans: ManagedPlans),

        // Mistral AI — BYOK only (§5.1).
        Model("mistral-large", "mistral-large", AiProviders.Mistral, "Mistral Large",
              hasVision: false, ctx: 128_000, jsonMode: true, streaming: true, plans: ManagedPlans),
        Model("mistral-medium", "mistral-medium", AiProviders.Mistral, "Mistral Medium",
              hasVision: false, ctx: 32_000, jsonMode: true, streaming: true, plans: ManagedPlans),
        Model("mistral-nemo", "mistral-nemo", AiProviders.Mistral, "Mistral Nemo",
              hasVision: false, ctx: 128_000, jsonMode: true, streaming: true, plans: ManagedPlans),
    ];

    public static IReadOnlyList<PlatformAiConfig> PlatformConfig { get; } =
    [
        PlatformDefault(AiFunctionIds.Copilot,      "claude-haiku-4-5",  AiKeySource.Platform),
        PlatformDefault(AiFunctionIds.NlYaml,       "claude-sonnet-4-6", AiKeySource.Platform),
        PlatformDefault(AiFunctionIds.VisualInput,  "claude-sonnet-4-6", AiKeySource.Platform),
        PlatformDefault(AiFunctionIds.NodeExec,     "claude-haiku-4-5",  AiKeySource.Byok),
        PlatformDefault(AiFunctionIds.ProcessIntel, "claude-haiku-4-5",  AiKeySource.Platform),
        PlatformDefault(AiFunctionIds.HelpAssist,   "claude-sonnet-4-6", AiKeySource.Platform),
        PlatformDefault(AiFunctionIds.DocParse,     "claude-sonnet-4-6", AiKeySource.Platform),
    ];

    private static ModelCatalogue Model(
        string modelId, string providerModelId, string provider, string displayName,
        bool hasVision, int ctx, bool jsonMode, bool streaming, IReadOnlyList<string> plans) =>
        new()
        {
            ModelId = modelId,
            ProviderModelId = providerModelId,
            Provider = provider,
            DisplayName = displayName,
            HasVision = hasVision,
            MaxContextTokens = ctx,
            SupportsJsonMode = jsonMode,
            SupportsStreaming = streaming,
            IsEnabled = true,
            PlanAccess = [.. plans],
            CreatedAt = SeedTimestamp,
            UpdatedAt = SeedTimestamp,
        };

    private static PlatformAiConfig PlatformDefault(
        string functionId, string modelId, AiKeySource keySource) =>
        new()
        {
            FunctionId = functionId,
            Provider = AiProviders.Anthropic,
            ModelId = modelId,
            KeySource = keySource,
            IsEnabled = true,
            PlanAccess = [.. AllPlans],
            CreatedAt = SeedTimestamp,
            UpdatedAt = SeedTimestamp,
        };
}
