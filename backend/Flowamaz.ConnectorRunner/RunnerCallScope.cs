namespace Flowamaz.ConnectorRunner;

/// <summary>
/// Per-call ambient state (AsyncLocal) read by the outbound HTTP pipeline, which is pooled by
/// IHttpClientFactory and therefore cannot take scoped services.
/// </summary>
internal static class RunnerCallScope
{
    private static readonly AsyncLocal<IReadOnlyList<string>?> Allowed = new();

    /// <summary>Hosts the current operation may call; null = any public host.</summary>
    public static IReadOnlyList<string>? AllowedDomains
    {
        get => Allowed.Value;
        set => Allowed.Value = value;
    }
}
