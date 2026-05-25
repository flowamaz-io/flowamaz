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
}
