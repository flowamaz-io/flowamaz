namespace Flowamaz.Core.Configuration;

/// <summary>Hard caps for the self-hosted Community edition (prompt 05-07). Binds the "Community" section.</summary>
public sealed class CommunityOptions
{
    public const string SectionName = "Community";

    public int MaxWorkflows { get; set; } = 5;
    public long MaxRunsPerMonth { get; set; } = 500;
    public int MaxUsers { get; set; } = 1;
}

/// <summary>Platform identity — which edition this deployment runs as. Binds the "Platform" section.</summary>
public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>community | starter | pro | enterprise. Sourced from the EDITION env var.</summary>
    public string Edition { get; set; } = "community";
}
