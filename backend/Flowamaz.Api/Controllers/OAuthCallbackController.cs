using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Public endpoint for OAuth 2.0 authorization-code callbacks.
/// No authentication required — this is the redirect target from the identity provider.
/// On success, returns an HTML page that posts a message to the opener window and closes itself.
/// </summary>
[ApiController]
[Route("api/v1/oauth")]
public sealed class OAuthCallbackController : ControllerBase
{
    private readonly IOAuthService _oauth;

    public OAuthCallbackController(IOAuthService oauth)
    {
        _oauth = oauth;
    }

    /// <summary>
    /// Receives the authorization code from the OAuth provider, exchanges it for a token, and
    /// returns an HTML snippet that closes the popup and posts a message to the opener.
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string code,
        [FromQuery] string state,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return Content(BuildErrorHtml("Missing 'code' or 'state' parameter. Restart the OAuth flow."), "text/html");
        }

        try
        {
            var credentialId = await _oauth.HandleCallbackAsync(code, state, cancellationToken);
            return Content(BuildSuccessHtml(credentialId), "text/html");
        }
        catch (InvalidOperationException ex)
        {
            return Content(BuildErrorHtml(ex.Message), "text/html");
        }
    }

    private static string BuildSuccessHtml(Guid credentialId)
    {
        var escapedId = System.Text.Json.JsonSerializer.Serialize(credentialId.ToString());
        return "<!DOCTYPE html><html><head><title>Authorization Complete</title></head><body>"
            + "<p>Authorization successful. This window will close automatically.</p>"
            + "<script>"
            + "try {"
            + $"window.opener.postMessage({{type:'oauth_complete',credentialId:{escapedId},success:true}},'*');"
            + "} catch(e) {}"
            + "window.close();"
            + "</script>"
            + "</body></html>";
    }

    private static string BuildErrorHtml(string errorMessage)
    {
        var escapedMsg = System.Text.Json.JsonSerializer.Serialize(errorMessage);
        var htmlMsg = System.Net.WebUtility.HtmlEncode(errorMessage);
        return "<!DOCTYPE html><html><head><title>Authorization Failed</title></head><body>"
            + $"<p>Authorization failed: {htmlMsg}</p>"
            + "<p>Please close this window and try again.</p>"
            + "<script>"
            + "try {"
            + $"window.opener.postMessage({{type:'oauth_complete',success:false,error:{escapedMsg}}},'*');"
            + "} catch(e) {}"
            + "</script>"
            + "</body></html>";
    }
}
