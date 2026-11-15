# Flowamaz.Connectors.Sdk

The contract for writing Flowamaz connectors in .NET (MIT).

- **`IConnectorOperationHandler`**: one operation of one connector (for example `slack/send-message`). The engine passes the node's inputs (already interpolated) and the decrypted credential, and gets back a JSON document. Return means success; throw `ConnectorOperationException` with an actionable message (and `retryable: true` for temporary failures) for anything else. Secrets come from the credential, never from inputs.
- **`ConnectorCredential`**: the credential a workspace configured for the connector. Its `ToString()` never shows the secret.
- **`ConnectorCallContext`**: who the call runs for (workspace, run, node), set by the engine. Use it to isolate per workspace; never trust a workspace id from inputs.
- **`ConnectorSpec` / `ConnectorOperationSpec`**: the manifest of a connector: operations, inputs, auth, allowed domains.
- **`EgressGuard`, `NetworkPolicy`**: refuse calls to private, loopback, link-local and cloud-metadata addresses, and non-HTTP URLs.
- **Host hooks:** `IEmailService` and `IRateLimitService`, which a host provides to connectors that need them.

Declarative HTTP connectors that need no code are described in `docs/connector-spec.md`. The official connectors in `Flowamaz.Connectors.Official` are worked examples of this SDK.

Moved code keeps its original namespaces (`Flowamaz.Core.*`, `Flowamaz.Application.Connectors`).
