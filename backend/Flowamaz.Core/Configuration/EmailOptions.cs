namespace Flowamaz.Core.Configuration;

/// <summary>
/// Binds the "Email" section of appsettings.json: Resend for transactional mail, or, when <see cref="SmtpHost"/> is
/// set, a plain SMTP server (for local development: Mailpit, see infrastructure/docker-compose.dev.yml).
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string ResendApiKey { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Flowamaz";
    public string SupportEmail { get; set; } = "support@flowamaz.io";
    public string SecurityEmail { get; set; } = "security@flowamaz.io";

    /// <summary>SMTP server to send through instead of Resend. Empty: Resend.</summary>
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 25;
    public bool SmtpEnableSsl { get; set; }
}
