namespace Flowamaz.Core.Exceptions;

/// <summary>
/// An SSO sign-in or SSO configuration change was refused. The message is shown to the person signing in (or the
/// admin) as-is, so it says what happened and what to do, and never includes assertion or token content.
/// </summary>
public sealed class SsoLoginException : AppException
{
    public SsoLoginException(string errorCode, string message, int httpStatusCode = 401)
        : base(errorCode, message, httpStatusCode) { }

    public static SsoLoginException NotOnPlan() => new("SSO_NOT_ON_PLAN",
        "Single sign-on is part of the Enterprise plan. Upgrade in Settings → Billing, or contact sales to enable it.", 403);

    public static SsoLoginException NotEnabled(string orgSlug) => new("SSO_NOT_ENABLED",
        $"Single sign-on isn't enabled for '{orgSlug}'. Sign in with your email and password, or ask your organisation owner.", 400);

    public static SsoLoginException Rejected(string why) => new("SSO_RESPONSE_REJECTED",
        $"Your identity provider's sign-in couldn't be verified: {why} Try again; if it keeps failing, ask your administrator to check the SSO settings.");

    public static SsoLoginException Expired() => new("SSO_SIGNIN_EXPIRED",
        "This sign-in took too long or was already used. Start again from the sign-in page.");

    public static SsoLoginException NoAccount() => new("SSO_NO_ACCOUNT",
        "You signed in with your identity provider, but you don't have a Flowamaz account in this organisation yet. Ask your administrator to invite you.", 403);

    public static SsoLoginException Deactivated() => new("SSO_ACCOUNT_DEACTIVATED",
        "Your Flowamaz account in this organisation is deactivated. Ask your administrator to reactivate it.", 403);

    public static SsoLoginException Required() => new("SSO_REQUIRED",
        "Your organisation signs in with single sign-on. Choose 'Sign in with SSO' instead of a password.", 403);
}
