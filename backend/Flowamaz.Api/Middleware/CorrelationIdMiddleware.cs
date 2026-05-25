using Serilog.Context;

namespace Flowamaz.Api.Middleware;

/// <summary>
/// Ensures every request carries an X-Correlation-Id end-to-end. Reads incoming header if
/// present (so traces line up across services) otherwise mints a new GUID. Pushes the value
/// into Serilog's <see cref="LogContext"/> so every log line inside the pipeline includes it,
/// and writes it back to the response so clients can quote it in support tickets.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    private const string LogPropertyName = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = string.IsNullOrWhiteSpace(incoming) ? Guid.NewGuid().ToString("N") : incoming;

        context.Items[LogPropertyName] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(LogPropertyName, correlationId))
        {
            await _next(context);
        }
    }
}
