using System.Text;
using System.Text.Json;
using FluentValidation;
using Flowamaz.Api.Middleware;
using Flowamaz.Core.Configuration;
using Flowamaz.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ──────────────────────────────────────────────────────────────────────────────
// 1. Configuration — env vars override appsettings; secrets never hardcoded.
// ──────────────────────────────────────────────────────────────────────────────
builder.Configuration.AddEnvironmentVariables();

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

// ──────────────────────────────────────────────────────────────────────────────
// 8. FluentValidation — register validators (assembly scan happens in later prompts
//    when Application gains DTOs/handlers; for now we just wire up the service).
// ──────────────────────────────────────────────────────────────────────────────
builder.Services.AddValidatorsFromAssembly(
    typeof(Flowamaz.Application.Ai.ModelCapabilityValidator).Assembly);

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
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(string.IsNullOrEmpty(jwtOptions.Secret)
                    // Bootstrap-only placeholder: lets the app start without a key, but every
                    // protected endpoint will 401 until JWT_SECRET is set. Prompt 04 hardens this.
                    ? new string('x', 64)
                    : jwtOptions.Secret)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

// ──────────────────────────────────────────────────────────────────────────────
// 12. Health checks — Postgres + Redis dependency probes.
// ──────────────────────────────────────────────────────────────────────────────
var dbConnection = builder.Configuration.GetConnectionString("DefaultConnection") ?? "";
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "";
builder.Services.AddHealthChecks()
    .AddNpgSql(dbConnection, name: "db", tags: ["db"])
    .AddRedis(redisConnection, name: "redis", tags: ["redis"]);

// ──────────────────────────────────────────────────────────────────────────────
// 13. OpenAPI + Scalar at /scalar (always public, no auth).
// ──────────────────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();
builder.Services.AddControllers();

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
