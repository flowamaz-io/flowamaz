namespace Flowamaz.Application.Connectors.Services;

/// <summary>
/// Thrown when an OAuth token exchange fails — provider returned an error, or the response
/// was missing required fields.
/// </summary>
public sealed class OAuthException : Exception
{
    public OAuthException(string message) : base(message) { }
    public OAuthException(string message, Exception inner) : base(message, inner) { }
}
