using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Models;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>Input accessors that fail with actionable, field-specific messages.</summary>
internal static class ConnectorInput
{
    public static string Required(JsonElement root, string name, string op)
    {
        var value = Optional(root, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new ConnectorOperationException($"{op}: input '{name}' is required. Set it in the node's inputs.");
        return value;
    }

    public static string? Optional(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => el.GetRawText(),
        };
    }

    public static bool Bool(JsonElement root, string name, bool fallback = false)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var el)) return fallback;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(el.GetString(), out var b) ? b : fallback,
            _ => fallback,
        };
    }

    public static int Int(JsonElement root, string name, int fallback, int min, int max)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var el)) return fallback;
        int value;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) value = n;
        else if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s)) value = s;
        else return fallback;
        return Math.Clamp(value, min, max);
    }

    /// <summary>Rejects secrets passed as plain inputs — they belong in the credential vault.</summary>
    public static void RejectSecretInput(JsonElement root, string op, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out _))
                throw new ConnectorOperationException(
                    $"{op}: '{name}' must not be set in the workflow inputs — secrets in YAML are visible to anyone who can read the workflow. " +
                    "Store it as a credential in Settings → Credentials and select it in the node's Credential field.");
        }
    }

    public static ConnectorCredential RequireCredential(ConnectorCredential? credential, string connector, string op) =>
        credential ?? throw new ConnectorOperationException(
            $"{op}: no {connector} credential is connected to this workspace. Connect one in Settings → Credentials, " +
            "then select it in the node's Credential field.");

    /// <summary>Escapes a value used as a single URL path segment (prevents path injection).</summary>
    public static string Segment(string value) => Uri.EscapeDataString(value);
}

/// <summary>Sends vendor API requests and turns non-success responses into ConnectorOperationException.</summary>
internal static class ConnectorHttp
{
    public static HttpRequestMessage Json(HttpMethod method, string url, object? body, string? bearer = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        if (!string.IsNullOrEmpty(bearer))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return request;
    }

    /// <summary>Sends and returns the parsed JSON body (or {"status_code":n} when empty). Throws on non-2xx.</summary>
    public static async Task<JsonDocument> SendAsync(HttpClient client, HttpRequestMessage request, string op, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectorOperationException($"{op}: could not reach {request.RequestUri?.Host}: {ex.Message}", retryable: true, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ConnectorOperationException($"{op}: {request.RequestUri?.Host} did not respond in time.", retryable: true, ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = text.Length > 300 ? text[..300] : text;
                var hint = status switch
                {
                    401 => " The credential was rejected — reconnect it in Settings → Credentials.",
                    403 => " The credential lacks permission for this action — check the connected account's scopes/permissions.",
                    404 => " The resource was not found — check the ids/names in the node inputs.",
                    429 => " Rate limited by the provider; the step will be retried per its retry policy.",
                    _ => string.Empty,
                };
                throw new ConnectorOperationException(
                    $"{op}: {request.RequestUri?.Host} returned HTTP {status}.{hint} Detail: {detail}",
                    ConnectorOperationException.IsRetryableStatus(status));
            }

            if (string.IsNullOrWhiteSpace(text))
                return JsonDocument.Parse($$"""{"status_code":{{status}}}""");
            try
            {
                return JsonDocument.Parse(text);
            }
            catch (JsonException)
            {
                return JsonDocument.Parse(JsonSerializer.Serialize(new { status_code = status, body = text }));
            }
        }
    }

    public static JsonDocument ToDocument(object value) => JsonDocument.Parse(JsonSerializer.Serialize(value));
}
