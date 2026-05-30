using System.Text.Json;

namespace Flowamaz.Api.Middleware;

/// <summary>
/// Wraps every successful 2xx JSON response in a consistent envelope:
/// <c>{ success, statusCode, data, correlationId }</c>. Skips bodies that aren't JSON
/// (Scalar UI, /health raw text, file downloads) and skips already-wrapped error responses
/// produced by <see cref="GlobalExceptionMiddleware"/>. Replaces the AutoWrapper package
/// dependency — same response shape, ~80 lines under our control.
/// </summary>
public sealed class ResponseWrapperMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] SkipPathPrefixes = ["/scalar", "/health", "/openapi", "/ws", "/api/public", "/api-docs"];

    private readonly RequestDelegate _next;

    public ResponseWrapperMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (ShouldSkip(context))
        {
            await _next(context);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);

            if (!IsWrappable(context))
            {
                buffer.Position = 0;
                await buffer.CopyToAsync(originalBody);
                return;
            }

            buffer.Position = 0;
            var bodyText = await new StreamReader(buffer).ReadToEndAsync();
            object? data = string.IsNullOrWhiteSpace(bodyText) ? null : TryParse(bodyText);

            context.Response.Body = originalBody;
            context.Response.ContentLength = null; // recalculated by serializer
            context.Response.ContentType = "application/json; charset=utf-8";

            var envelope = new
            {
                success = context.Response.StatusCode is >= 200 and < 300,
                statusCode = context.Response.StatusCode,
                data,
                correlationId = context.Items.TryGetValue("CorrelationId", out var v) && v is string s ? s : "",
            };
            await JsonSerializer.SerializeAsync(originalBody, envelope, JsonOptions);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static bool ShouldSkip(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        foreach (var prefix in SkipPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsWrappable(HttpContext context)
    {
        var status = context.Response.StatusCode;
        if (status is < 200 or >= 300) return false; // GlobalExceptionMiddleware owns errors
        // 204 No Content, 205 Reset Content, and 304 Not Modified MUST NOT carry a body
        // per RFC 9110 §15.3.5/15.3.6/15.4.5 — wrapping would inject one and break caches/clients.
        if (status is 204 or 205 or 304) return false;
        var contentType = context.Response.ContentType ?? "";
        return contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static object? TryParse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return body;
        }
    }
}
