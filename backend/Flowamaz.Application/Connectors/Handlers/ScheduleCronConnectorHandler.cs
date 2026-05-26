using System.Text.Json;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// Validates cron expressions. This is a trigger connector — no actual scheduling in Phase 4.
/// Input: { cron_expression: string }
/// Returns: { valid: bool, description?: string }
/// </summary>
public sealed class ScheduleCronValidateHandler : IConnectorOperationHandler
{
    private readonly ILogger<ScheduleCronValidateHandler> _logger;

    public ScheduleCronValidateHandler(ILogger<ScheduleCronValidateHandler> logger)
    {
        _logger = logger;
    }

    public string ConnectorId => "schedule-cron";
    public string OperationId => "validate-cron";

    public Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct)
    {
        _logger.LogInformation("[ScheduleCron:validate-cron] ExecuteAsync entry");

        var root = input.RootElement;
        var expression = root.GetProperty("cron_expression").GetString()
            ?? throw new InvalidOperationException("'cron_expression' is required.");

        var (valid, description) = ValidateCron(expression);
        var descPart = description is not null ? $",\"description\":{JsonSerializer.Serialize(description)}" : string.Empty;
        var result = JsonDocument.Parse($"{{\"valid\":{valid.ToString().ToLowerInvariant()}{descPart}}}");

        _logger.LogInformation("[ScheduleCron:validate-cron] ExecuteAsync exit valid={Valid}", valid);
        return Task.FromResult(result);
    }

    private static (bool Valid, string? Description) ValidateCron(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return (false, "Cron expression cannot be empty.");

        var parts = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 5 or > 6)
            return (false, $"Expected 5 or 6 fields, got {parts.Length}.");

        // Basic field validation — each part must be *, a number, or contain valid cron operators
        foreach (var part in parts)
        {
            if (!IsValidCronField(part))
                return (false, $"Invalid cron field: '{part}'.");
        }

        return (true, $"Valid cron expression with {parts.Length} fields.");
    }

    private static bool IsValidCronField(string field)
    {
        if (field == "*" || field == "?") return true;

        // Allow: digits, *, /, -, ,
        foreach (var ch in field)
        {
            if (!char.IsDigit(ch) && ch is not '*' and not '/' and not '-' and not ',' and not '?')
                return false;
        }
        return true;
    }
}
