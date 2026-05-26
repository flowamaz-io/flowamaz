using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Services;

/// <summary>
/// Calls a BYOM endpoint using an OpenAI-compatible chat completions API. The endpoint URL and
/// API key are stored as a JSON credential in the vault: {"url":"https://...","api_key":"..."}.
/// </summary>
public sealed class ByomProviderService : IByomProviderService
{
    public const string HttpClientName = "byom-http";

    private readonly ICredentialVaultService _vault;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ByomProviderService> _logger;

    public ByomProviderService(
        ICredentialVaultService vault,
        IHttpClientFactory httpClientFactory,
        ILogger<ByomProviderService> logger)
    {
        _vault = vault;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> CompleteAsync(
        Guid workspaceId,
        Guid byomCredentialId,
        string model,
        string prompt,
        int maxTokens,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "ByomProviderService.CompleteAsync enter workspace={WorkspaceId} credential={CredentialId} model={Model}",
            workspaceId, byomCredentialId, model);

        var credJson = await _vault.RetrieveAsync(workspaceId, byomCredentialId, ct);

        string baseUrl;
        string apiKey;
        try
        {
            using var doc = JsonDocument.Parse(credJson);
            var root = doc.RootElement;
            baseUrl = root.GetProperty("url").GetString()
                ?? throw new InvalidOperationException("BYOM credential is missing 'url'.");
            apiKey = root.GetProperty("api_key").GetString()
                ?? throw new InvalidOperationException("BYOM credential is missing 'api_key'.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"BYOM credential {byomCredentialId} could not be parsed. Ensure it is JSON with 'url' and 'api_key' keys.", ex);
        }

        var requestBody = new
        {
            model,
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = maxTokens
        };

        var json = JsonSerializer.Serialize(requestBody);
        var endpoint = baseUrl.TrimEnd('/') + "/v1/chat/completions";

        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await SafeReadAsync(response);
            throw new HttpRequestException(
                $"BYOM endpoint returned HTTP {(int)response.StatusCode}: {errorBody}. " +
                "Check the endpoint URL and API key in your BYOM credential.");
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        using var responseDoc = JsonDocument.Parse(responseBody);

        var content = responseDoc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (content is null)
            throw new InvalidOperationException(
                "BYOM endpoint response did not contain a valid 'choices[0].message.content'. " +
                "Ensure the endpoint returns an OpenAI-compatible chat completions response.");

        _logger.LogInformation(
            "ByomProviderService.CompleteAsync exit workspace={WorkspaceId} credential={CredentialId}",
            workspaceId, byomCredentialId);

        return content;
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            return body.Length > 400 ? body[..400] : body;
        }
        catch
        {
            return response.ReasonPhrase ?? "no detail";
        }
    }
}
