using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flowamaz.Api.Middleware;
using Flowamaz.Api.WebSockets;
using Flowamaz.Application;
using Flowamaz.Application.Health;
using Flowamaz.Core.Configuration;
using Flowamaz.Infrastructure;
using Flowamaz.Infrastructure.Jobs;
using FluentValidation;
using Quartz;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;

// Bootstrap logger — captures startup-time logs (e.g. the JWT secret warning below) before
// the host's Serilog pipeline is built. Replaced by the full configuration in UseSerilog.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter())
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

// ──────────────────────────────────────────────────────────────────────────────
// 1. Configuration — env vars override appsettings; secrets never hardcoded.
// ──────────────────────────────────────────────────────────────────────────────
builder.Configuration.AddEnvironmentVariables();

// The documented single-name JWT_* env vars (FUNCTIONAL.md §22 / .env.example) don't bind to the
// Jwt:* keys the app reads (.NET would expect Jwt__Secret). Map them on, mirroring how
// ConnectionStringResolver prefers DB_CONNECTION_STRING/REDIS_CONNECTION_STRING. Added last so they win.
var jwtEnvMap = new Dictionary<string, string?>();
foreach (var (envVar, configKey) in new[]
{
    ("JWT_SECRET", "Jwt:Secret"),
    ("JWT_ISSUER", "Jwt:Issuer"),
    ("JWT_AUDIENCE", "Jwt:Audience"),
    ("JWT_ACCESS_TOKEN_EXPIRY_MINUTES", "Jwt:AccessTokenExpiryMinutes"),
    ("JWT_REFRESH_TOKEN_EXPIRY_DAYS", "Jwt:RefreshTokenExpiryDays"),
})
{
    var value = builder.Configuration[envVar];
    if (!string.IsNullOrWhiteSpace(value)) jwtEnvMap[configKey] = value;
}
if (jwtEnvMap.Count > 0) builder.Configuration.AddInMemoryCollection(jwtEnvMap);

// ──────────────────────────────────────────────────────────────────────────────
// 2. Serilog — JSON to console + rolling file, CorrelationId enricher from LogContext.
// ──────────────────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "Flowamaz.Api")
        .WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter())
        .WriteTo.File(
            path: "logs/flowamaz-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            formatter: new Serilog.Formatting.Compact.RenderedCompactJsonFormatter());
});

// ──────────────────────────────────────────────────────────────────────────────
// 3–7. Infrastructure (DbContext, Redis, AI services, Email) via composition root.
// ──────────────────────────────────────────────────────────────────────────────
builder.Services.AddInfrastructure(builder.Configuration);

// CurrentUserService reads the identity JwtAuthMiddleware resolved into HttpContext.Items.
// Registered here (not Infrastructure) so Infrastructure stays free of the web framework.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Flowamaz.Core.Interfaces.Services.ICurrentUserService, Flowamaz.Api.Identity.HttpContextCurrentUserService>();
builder.Services.AddSingleton<Flowamaz.Core.Interfaces.Services.IRequestContextAccessor, Flowamaz.Api.Identity.HttpRequestContextAccessor>();

// Live instance-status WebSocket handler (prompt 02-04). Uses IServiceScopeFactory internally.
builder.Services.AddSingleton<InstanceStatusWebSocketHandler>();

// ──────────────────────────────────────────────────────────────────────────────
// 8. Application layer — services + FluentValidation validators (one assembly scan).
// ──────────────────────────────────────────────────────────────────────────────
builder.Services.AddApplication();

// Api-layer request validators (DTOs declared on the controllers themselves): billing checkout/
// portal redirect allowlist, template publish/install, replay payload + breakpoint requests.
builder.Services.AddValidatorsFromAssemblyContaining<Flowamaz.Api.Validators.CheckoutRequestValidator>();

// ──────────────────────────────────────────────────────────────────────────────
// 10. CORS — origin allowlist from CORS_ALLOWED_ORIGINS env var (comma separated).
// ──────────────────────────────────────────────────────────────────────────────
const string CorsPolicyName = "FlowamazCors";
var configuredOrigins = (builder.Configuration["CORS_ALLOWED_ORIGINS"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var corsOrigins = Flowamaz.Api.Cors.CorsOriginPolicy.Resolve(
    builder.Environment.IsDevelopment(), configuredOrigins, builder.Configuration["MARKETING_SITE_URL"]);
if (corsOrigins.Length == 0 && !builder.Environment.IsDevelopment())
{
    // Production MUST set CORS_ALLOWED_ORIGINS — log a warning so the misconfiguration is visible.
    Log.Warning("CORS_ALLOWED_ORIGINS is not set in Production. All cross-origin requests will be blocked.");
}
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
    });
});

// ──────────────────────────────────────────────────────────────────────────────
// 11. JWT — scheme registration only. Endpoints will require it in prompt 04.
// ──────────────────────────────────────────────────────────────────────────────
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

SymmetricSecurityKey signingKey;
if (!string.IsNullOrEmpty(jwtOptions.Secret))
{
    signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret));
}
else if (builder.Environment.IsDevelopment())
{
    // No deterministic placeholder: a random per-process key means leaked source can't be used to
    // mint tokens, and developers are nudged to set a real secret. Tokens won't survive a restart.
    Log.Warning(
        "JWT signing secret is not configured. Generating a RANDOM EPHEMERAL key for this Development " +
        "process only — tokens will be invalidated on restart. Set Jwt__Secret (JWT_SECRET) for stable auth.");
    signingKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64));
}
else
{
    throw new InvalidOperationException(
        "JWT signing secret is not configured. Set Jwt__Secret (env var JWT_SECRET) to a value of at " +
        "least 32 bytes before starting outside the Development environment.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

// ──────────────────────────────────────────────────────────────────────────────
// 12. Health checks — Postgres + Redis dependency probes.
// ──────────────────────────────────────────────────────────────────────────────
var dbConnection = ConnectionStringResolver.ResolveDatabase(builder.Configuration);
var redisConnection = ConnectionStringResolver.ResolveRedis(builder.Configuration);

// Named HttpClients for the third-party probes — kept tiny so a hung dependency can't stall /health.
builder.Services.AddHttpClient(StripeHealthCheck.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddHttpClient(AnthropicHealthCheck.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddHttpClient(ResendHealthCheck.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(3));

builder.Services.AddHealthChecks()
    // Critical dependencies — Unhealthy when down → overall Unhealthy → HTTP 503.
    .AddNpgSql(dbConnection, name: "database", tags: ["db", "critical"])
    .AddRedis(redisConnection, name: "redis", tags: ["redis", "critical"])
    // Third-party probes — NON-CRITICAL. failureStatus Degraded ensures an unreachable third party
    // reports degraded (HTTP 200), never flipping /health to 503. Only db/redis can do that.
    .AddCheck<StripeHealthCheck>("stripe", failureStatus: HealthStatus.Degraded, tags: ["external"])
    .AddCheck<AnthropicHealthCheck>("anthropic", failureStatus: HealthStatus.Degraded, tags: ["external"])
    .AddCheck<ResendHealthCheck>("resend", failureStatus: HealthStatus.Degraded, tags: ["external"]);

// ──────────────────────────────────────────────────────────────────────────────
// 12b. Quartz — delayed-queue promoter (5s), lease-expiry recovery (15s), gate
//      timeout/escalation (60s). Disabled with Worker:Enabled=false (integration tests).
// ──────────────────────────────────────────────────────────────────────────────
var backgroundWorkersEnabled = builder.Configuration.GetValue<bool?>("Worker:Enabled") ?? true;
if (backgroundWorkersEnabled)
{
    builder.Services.AddQuartz(q =>
    {
        var promoterKey = new JobKey(nameof(DelayedQueuePromoterJob));
        q.AddJob<DelayedQueuePromoterJob>(promoterKey);
        q.AddTrigger(t => t.ForJob(promoterKey)
            .WithSimpleSchedule(s => s.WithIntervalInSeconds(5).RepeatForever()));

        // Lease-expiry recovery every 15s (prompt 02-03).
        var leaseKey = new JobKey(nameof(WorkerLeaseExpiryJob));
        q.AddJob<WorkerLeaseExpiryJob>(leaseKey);
        q.AddTrigger(t => t.ForJob(leaseKey)
            .WithSimpleSchedule(s => s.WithIntervalInSeconds(15).RepeatForever()));

        // Gate timeout / escalation every 60s (prompt 02-03).
        var gateKey = new JobKey(nameof(GateTimeoutJob));
        q.AddJob<GateTimeoutJob>(gateKey);
        q.AddTrigger(t => t.ForJob(gateKey)
            .WithSimpleSchedule(s => s.WithIntervalInSeconds(60).RepeatForever()));

        // Process Intelligence hourly, with up to 5min jitter to avoid a thundering herd (prompt 02-07).
        var intelKey = new JobKey(nameof(ProcessIntelligenceJob));
        q.AddJob<ProcessIntelligenceJob>(intelKey);
        q.AddTrigger(t => t.ForJob(intelKey)
            .StartAt(DateBuilder.FutureDate(Random.Shared.Next(0, 300), IntervalUnit.Second))
            .WithSimpleSchedule(s => s.WithIntervalInHours(1).RepeatForever()));

        // Batch result poller every 30 minutes — picks up F5 Process Intelligence batch results.
        var batchPollerKey = new JobKey(nameof(BatchResultPollerJob));
        q.AddJob<BatchResultPollerJob>(batchPollerKey);
        q.AddTrigger(t => t.ForJob(batchPollerKey)
            .WithSimpleSchedule(s => s.WithIntervalInMinutes(30).RepeatForever()));

        // Test instance cleanup daily at 03:00 UTC.
        var testCleanupKey = new JobKey(nameof(TestInstanceCleanupJob));
        q.AddJob<TestInstanceCleanupJob>(testCleanupKey);
        q.AddTrigger(t => t.ForJob(testCleanupKey)
            .WithCronSchedule("0 0 3 * * ?"));

        // One-time Git repo backfill — fires once on startup (prompt 05-01). Idempotent.
        var gitInitKey = new JobKey(nameof(WorkspaceGitInitialisationJob));
        q.AddJob<WorkspaceGitInitialisationJob>(gitInitKey);
        q.AddTrigger(t => t.ForJob(gitInitKey).StartNow());

        // Audit-log retention (prompt 07-02) — daily at 03:30 UTC. Deletes events past the plan window.
        var auditRetentionKey = new JobKey(nameof(AuditRetentionJob));
        q.AddJob<AuditRetentionJob>(auditRetentionKey);
        q.AddTrigger(t => t.ForJob(auditRetentionKey)
            .WithCronSchedule("0 30 3 * * ?"));
    });
    builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
}

// ──────────────────────────────────────────────────────────────────────────────
// 13. OpenAPI + Scalar at /scalar (always public, no auth).
// ──────────────────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();
// Action filter that 404s dev-only endpoints (breakpoint/resume/step) outside Development.
builder.Services.AddScoped<Flowamaz.Api.Filters.DevelopmentOnlyFilter>();
// Second OpenAPI document exposing ONLY the public developer API (/api/public/*) at /api-docs.
builder.Services.AddOpenApi("public", options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Flowamaz Public API";
        document.Info.Description = "REST API for triggering workflows and reading instances.";
        var publicPaths = document.Paths
            .Where(p => p.Key.StartsWith("/api/public", StringComparison.OrdinalIgnoreCase))
            .ToList();
        document.Paths.Clear();
        foreach (var path in publicPaths)
            document.Paths.Add(path.Key, path.Value);
        return Task.CompletedTask;
    });
});
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialise/accept enums as strings (e.g. role "Admin", marketplacePolicy "AllowAll")
        // rather than opaque integers, on both responses and request binding.
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        // Case-insensitive binding so camelCase JSON from Vue matches PascalCase C# properties.
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

var app = builder.Build();

// ──────────────────────────────────────────────────────────────────────────────
// 14. Pipeline: Serilog → CorrelationId → CORS → Exception → ResponseWrapper → Auth → Endpoints.
// ──────────────────────────────────────────────────────────────────────────────
app.UseSerilogRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseCors(CorsPolicyName);
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<ResponseWrapperMiddleware>();
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();
// Resolves JWT/API-key identity into HttpContext.Items for the current-user service.
app.UseMiddleware<JwtAuthMiddleware>();

app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithTitle("Flowamaz API")
           .WithTheme(ScalarTheme.Default);
});
// Public developer API docs — public endpoints only.
app.MapScalarApiReference("/api-docs", options =>
{
    options.WithTitle("Flowamaz Public API")
           .WithOpenApiRoutePattern("/openapi/public.json")
           .WithTheme(ScalarTheme.Default);
});

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = WriteHealthResponse,
    // Degraded (a non-critical third party is down/slow) stays HTTP 200; only Unhealthy
    // (database or Redis down) returns 503. Healthy and Degraded both map to 200.
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    },
}).AllowAnonymous();

app.MapControllers();

// Live instance status stream (prompt 02-04). Anonymous endpoint — the handler validates the
// ?token= JWT itself, since browsers cannot set Authorization headers on a WebSocket.
app.Map("/ws/v1/workspaces/{workspaceId:guid}/instances/{id:guid}",
    (HttpContext ctx, Guid workspaceId, Guid id, InstanceStatusWebSocketHandler handler) =>
        handler.HandleAsync(ctx, workspaceId, id));

// ──────────────────────────────────────────────────────────────────────────────
// 15. Apply pending EF Core migrations on boot so `docker compose up` self-bootstraps
//     the schema — no manual `dotnet ef database update`. Failure stops startup.
// ──────────────────────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var migrationService = scope.ServiceProvider
        .GetRequiredService<Flowamaz.Core.Interfaces.Services.IStartupMigrationService>();
    await migrationService.RunAsync(app.Lifetime.ApplicationStopping);
}

// ──────────────────────────────────────────────────────────────────────────────
// 16. Security startup gates — reject misconfigured deployments before first request.
// ──────────────────────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    // Required production secrets. Each is read either as a single-name env var (mapped to config
    // earlier, e.g. JWT_SECRET) or via Section__Key binding (e.g. Email__ResendApiKey). A missing
    // value stops startup with an actionable message rather than failing silently at first use.
    // (config key, friendly env var, how to generate). DB is resolved from either
    // DB_CONNECTION_STRING or ConnectionStrings:DefaultConnection.
    var requiredSecrets = new (Func<string?> Read, string EnvVar, string HowToGenerate)[]
    {
        (() => app.Configuration["DB_CONNECTION_STRING"] ?? app.Configuration.GetConnectionString("DefaultConnection"),
            "DB_CONNECTION_STRING",
            "Set to your PostgreSQL connection string (Host=...;Database=...;Username=...;Password=...)."),
        (() => app.Configuration["Jwt:Secret"], "JWT_SECRET", "Generate with: openssl rand -base64 48"),
        (() => app.Configuration["CREDENTIAL_MASTER_KEY"], "CREDENTIAL_MASTER_KEY", "Generate with: openssl rand -base64 32"),
        (() => app.Configuration["GATE_SIGNING_KEY"], "GATE_SIGNING_KEY", "Generate with: openssl rand -hex 32"),
        (() => app.Configuration["Email:ResendApiKey"], "Email__ResendApiKey", "Copy from the Resend dashboard → API Keys (re_...)."),
        (() => app.Configuration["Ai:AnthropicPlatformKey"], "Ai__AnthropicPlatformKey", "Copy from the Anthropic console → API Keys (sk-ant-...)."),
    };

    var missing = new List<string>();
    foreach (var (read, envVar, how) in requiredSecrets)
    {
        if (string.IsNullOrWhiteSpace(read()))
            missing.Add($"  - {envVar}: {how}");
    }

    if (missing.Count > 0)
        throw new InvalidOperationException(
            "The following required environment variables are not set in a non-Development environment. " +
            "Set each before starting the service:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
}

// Git repos base path must exist and be writable — workflow versioning depends on it (prompt 05-01).
// Mirror WorkspaceGitService's resolution: Git:ReposBasePath → GIT_REPOS_BASE_PATH → bin/git-repos.
var gitReposBasePath = app.Configuration["Git:ReposBasePath"];
if (string.IsNullOrWhiteSpace(gitReposBasePath))
    gitReposBasePath = app.Configuration["GIT_REPOS_BASE_PATH"];
if (string.IsNullOrWhiteSpace(gitReposBasePath))
    gitReposBasePath = Path.Combine(AppContext.BaseDirectory, "git-repos");
try
{
    Directory.CreateDirectory(gitReposBasePath);
    var probe = Path.Combine(gitReposBasePath, $".write-probe-{Guid.NewGuid():N}");
    await File.WriteAllTextAsync(probe, "ok", app.Lifetime.ApplicationStopping);
    File.Delete(probe);
    Log.Information("Git repos base path is writable: {Path}", gitReposBasePath);
}
catch (Exception ex)
{
    throw new InvalidOperationException(
        $"GIT_REPOS_BASE_PATH '{gitReposBasePath}' is not writable. Workflow Git versioning requires a " +
        "writable directory. Create it and grant the service write access, or set GIT_REPOS_BASE_PATH.", ex);
}

try
{
    Log.Information("Flowamaz.Api starting — environment={Environment}", app.Environment.EnvironmentName);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Flowamaz.Api host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// ─── helpers ──────────────────────────────────────────────────────────────────

static async Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";

    var environment = context.RequestServices.GetRequiredService<IHostEnvironment>().EnvironmentName;
    var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

    var summary = Flowamaz.Application.Health.HealthCheckService.Compose(report, version, environment);
    await JsonSerializer.SerializeAsync(context.Response.Body, summary,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

namespace Flowamaz.Api
{
    /// <summary>Marker for WebApplicationFactory in integration tests.</summary>
    public partial class Program;
}
