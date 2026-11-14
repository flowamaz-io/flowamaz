namespace Flowamaz.Core.Configuration;

/// <summary>Binds the "Jwt" section of appsettings.json. Used by JWT bearer setup and token issuer.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "flowamaz-api";
    public string Audience { get; set; } = "flowamaz-client";
    public int AccessTokenExpiryMinutes { get; set; } = 15;
    public int RefreshTokenExpiryDays { get; set; } = 7;

    /// <summary>
    /// A signed-in `fmz` CLI stays signed in while it is used at least once in this many days
    /// (each refresh pushes the end date out again). Env: Jwt__CliSessionIdleDays.
    /// </summary>
    public int CliSessionIdleDays { get; set; } = 30;
}
