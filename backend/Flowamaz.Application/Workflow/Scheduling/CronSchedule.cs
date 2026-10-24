using Cronos;

namespace Flowamaz.Application.Workflow.Scheduling;

/// <summary>
/// Cron parsing shared by the schedule-cron connector and the schedule trigger job. Accepts the
/// standard 5-field form and the 6-field form with seconds. Time zones accept IANA or Windows ids.
/// </summary>
public static class CronSchedule
{
    /// <summary>Smallest allowed gap between runs; protects the platform from per-second schedules.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);

    public sealed record Parsed(CronExpression Expression, TimeZoneInfo TimeZone);

    public static bool TryParse(string? expression, string? timeZoneId, out Parsed? parsed, out string error)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Cron expression is empty. Example: '0 9 * * 1-5' runs at 09:00 on weekdays.";
            return false;
        }

        var fields = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (fields is not (5 or 6))
        {
            error = $"Expected 5 fields (minute hour day month weekday) or 6 with seconds, got {fields}.";
            return false;
        }

        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(expression.Trim(), fields == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard);
        }
        catch (CronFormatException ex)
        {
            error = $"Invalid cron expression: {ex.Message}";
            return false;
        }

        TimeZoneInfo tz;
        try
        {
            tz = string.IsNullOrWhiteSpace(timeZoneId) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            error = $"Unknown time zone '{timeZoneId}'. Use an IANA id such as 'Asia/Kuala_Lumpur' or 'UTC'.";
            return false;
        }

        var first = cron.GetNextOccurrence(DateTime.UtcNow, tz);
        var second = first is null ? null : cron.GetNextOccurrence(first.Value, tz);
        if (first is not null && second is not null && second.Value - first.Value < MinimumInterval)
        {
            error = $"Schedules may run at most once per minute; '{expression}' runs every {(second.Value - first.Value).TotalSeconds:0}s.";
            return false;
        }

        parsed = new Parsed(cron, tz);
        error = string.Empty;
        return true;
    }

    public static IReadOnlyList<DateTime> NextOccurrences(Parsed parsed, DateTime fromUtc, int count)
    {
        var result = new List<DateTime>(count);
        var cursor = fromUtc;
        for (var i = 0; i < count; i++)
        {
            var next = parsed.Expression.GetNextOccurrence(cursor, parsed.TimeZone);
            if (next is null) break;
            result.Add(next.Value);
            cursor = next.Value;
        }
        return result;
    }
}
