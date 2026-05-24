namespace Flowamaz.Core.Configuration;

/// <summary>Binds the "Email" section of appsettings.json — Resend config for transactional mail.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string ResendApiKey { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "noreply@flowamaz.io";
    public string FromName { get; set; } = "Flowamaz";
    public string SupportEmail { get; set; } = "support@flowamaz.io";
    public string SecurityEmail { get; set; } = "security@flowamaz.io";
}
