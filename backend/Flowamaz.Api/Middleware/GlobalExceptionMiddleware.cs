using System.Text.Json;
using Flowamaz.Core.Exceptions;

namespace Flowamaz.Api.Middleware;

/// <summary>
/// Catches every unhandled exception, logs it with the correlation ID, and returns a
/// structured JSON envelope. Never returns a stack trace to the client.
/// Per CLAUDE.md rule 8 every error message is actionable — for AppException we use the
/// already-actionable message; for anything else we return a stable code and a generic
/// recovery hint plus the correlation ID for support.
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException appEx)
        {
            _logger.LogWarning(appEx,
                "AppException handled — code={ErrorCode} status={Status} message={Message}",
                appEx.ErrorCode, appEx.HttpStatusCode, appEx.Message);
            await WriteJsonAsync(context, appEx.HttpStatusCode, new ErrorResponse(
                Success: false,
                StatusCode: appEx.HttpStatusCode,
                Code: appEx.ErrorCode,
                Message: appEx.Message,
                CorrelationId: ResolveCorrelationId(context)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled exception — path={Path} method={Method}",
                context.Request.Path, context.Request.Method);
            await WriteJsonAsync(context, 500, new ErrorResponse(
                Success: false,
                StatusCode: 500,
                Code: "INTERNAL_ERROR",
                Message: "An unexpected error occurred. Quote the correlation ID when contacting support@flowamaz.io.",
                CorrelationId: ResolveCorrelationId(context)));
        }
    }

    private static string ResolveCorrelationId(HttpContext context) =>
        context.Items.TryGetValue("CorrelationId", out var value) && value is string s ? s : "";

    private static async Task WriteJsonAsync(HttpContext context, int status, ErrorResponse body)
    {
        if (context.Response.HasStarted) return; // can't replace a partially-written response
        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, body, JsonOptions);
    }

    private sealed record ErrorResponse(
        bool Success,
        int StatusCode,
        string Code,
        string Message,
        string CorrelationId);
}
