namespace Flowamaz.Application.Connectors;

/// <summary>One operation of a connector, as published in its manifest.</summary>
/// <param name="Internal">Used by the platform itself (e.g. gate approval cards); hidden from the node config UI.</param>
public sealed record ConnectorOperationSpec(
    string Id,
    string Name,
    string Description,
    bool RequiresCredential,
    IReadOnlyList<string> Required,
    IReadOnlyDictionary<string, (string Type, string Description)> Properties,
    bool Internal = false);

/// <param name="AllowedDomains">Hosts the connector may call (subdomains included); null = any public host.</param>
/// <param name="Sandboxed">Runs in the isolated connector-runner container when one is configured (§9.5).</param>
public sealed record ConnectorSpec(
    string ConnectorId,
    string AuthType,
    IReadOnlyList<string> CredentialTypes,
    IReadOnlyList<ConnectorOperationSpec> Operations,
    IReadOnlyList<string>? Triggers = null,
    IReadOnlyList<string>? AllowedDomains = null,
    bool Sandboxed = false);
