using System.Text.Json;
using Flowamaz.Application.Workflow.Scheduling;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// Validates a cron expression and returns its next five run times. Scheduling itself is done by
/// the schedule trigger job for workflows whose trigger type is <c>schedule</c>.
/// Input: { expression, timezone? } (legacy key <c>cron_expression</c> accepted).
/// Returns: { valid, error?, next_runs_utc[] }.
/// </summary>
public sealed class ScheduleCronValidateHandler : IConnectorOperationHandler
{
    private readonly ILogger<ScheduleCronValidateHandler> _logger;

    public ScheduleCronValidateHandler(ILogger<ScheduleCronValidateHandler> logger) => _logger = logger;

    public string ConnectorId => "schedule-cron";
    public string OperationId => "validate-cron";

    public Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        _logger.LogInformation("[ScheduleCron:validate-cron] ExecuteAsync entry");
        var root = input.RootElement;
        var expression = ConnectorInput.Optional(root, "expression") ?? ConnectorInput.Optional(root, "cron_expression");
        var timezone = ConnectorInput.Optional(root, "timezone");

        JsonDocument result;
        if (CronSchedule.TryParse(expression, timezone, out var parsed, out var error))
        {
            var next = CronSchedule.NextOccurrences(parsed!, DateTime.UtcNow, 5).Select(d => d.ToString("O"));
            result = ConnectorHttp.ToDocument(new { valid = true, next_runs_utc = next });
        }
        else
        {
            result = ConnectorHttp.ToDocument(new { valid = false, error, next_runs_utc = Array.Empty<string>() });
        }

        _logger.LogInformation("[ScheduleCron:validate-cron] ExecuteAsync exit");
        return Task.FromResult(result);
    }
}
