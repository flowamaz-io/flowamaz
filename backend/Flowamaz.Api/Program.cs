using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flowamaz.Api.Middleware;
using Flowamaz.Application;
using Flowamaz.Core.Configuration;
using Flowamaz.Infrastructure;
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

// ──────────────────────────────────────────────────────────────────────────────
// 8. Application layer — services + FluentValidation validators (one assembly scan).
// ──────────────────────────────────────────────────────────────────────────────
builder.Services.AddApplication();

// ──────────────────────────────────────────────────────────────────────────────
// 10. CORS — origin allowlist from CORS_ALLOWED_ORIGINS env var (comma separated).
// ──────────────────────────────────────────────────────────────────────────────
const string CorsPolicyName = "FlowamazCors";
var allowedOrigins = (builder.Configuration["CORS_ALLOWED_ORIGINS"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            // Dev fallback — Vite default port. Production MUST set CORS_ALLOWED_ORIGINS.
            policy.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
        else
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
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
builder.Services.AddHealthChecks()
    .AddNpgSql(dbConnection, name: "db", tags: ["db"])
    .AddRedis(redisConnection, name: "redis", tags: ["redis"]);

// ──────────────────────────────────────────────────────────────────────────────
// 13. OpenAPI + Scalar at /scalar (always public, no auth).
// ──────────────────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        // Serialise/accept enums as strings (e.g. role "Admin", marketplacePolicy "AllowAll")
        // rather than opaque integers, on both responses and request binding.
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

// ──────────────────────────────────────────────────────────────────────────────
// 14. Pipeline: Serilog → CorrelationId → CORS → Exception → ResponseWrapper → Auth → Endpoints.
// ──────────────────────────────────────────────────────────────────────────────
app.UseSerilogRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseCors(CorsPolicyName);
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<ResponseWrapperMiddleware>();
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

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = WriteHealthResponse,
}).AllowAnonymous();

app.MapControllers();

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
    var entries = new Dictionary<string, string>();
    foreach (var entry in report.Entries)
    {
        entries[entry.Key] = entry.Value.Status.ToString().ToLowerInvariant();
    }
    var payload = new
    {
        status = report.Status.ToString().ToLowerInvariant(),
        totalDurationMs = report.TotalDuration.TotalMilliseconds,
        dependencies = entries,
    };
    await JsonSerializer.SerializeAsync(context.Response.Body, payload,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

namespace Flowamaz.Api
{
    /// <summary>Marker for WebApplicationFactory in integration tests.</summary>
    public partial class Program;
}
