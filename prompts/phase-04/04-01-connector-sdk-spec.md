---
prompt-id: 04-01-connector-sdk-spec
phase: 04
sequence: 1
roles: [Executor, Verifier, Security]
type: feature
depends-on: []
estimated-complexity: High
---

# Connector SDK + Specification + Credential Vault

## Context
Phase 3 complete. Phase 4 builds the connector ecosystem and AI execution layer.
This prompt establishes the connector specification, the SDK that contributors use
to build connectors, and the credential vault that stores OAuth tokens and API keys.
See FUNCTIONAL.md §9 (connector ecosystem).

## Objective
connector.yaml manifest specification, TypeScript connector SDK (@flowamaz/connector-sdk),
credential vault (AES-256 per-workspace encryption), and the connector execution sandbox.

## Scope

### What to Build

**Connector Manifest Specification (connector.yaml):**

Every connector declares:
```yaml
id: xero-accounting
publisher_id: flowamaz-io
display_name: "Xero Accounting"
version: 1.0.0
description: "Invoices, bills, bank reconciliation, contacts, reports"
category: finance
tags: [accounting, invoicing, reconciliation]
min_engine_version: 1.0.0
licence: MIT

auth:
  type: oauth2-auth-code | oauth2-client-credentials | api-key | basic | custom
  oauth2:
    authorization_url: https://login.xero.com/identity/connect/authorize
    token_url: https://identity.xero.com/connect/token
    scopes: [openid, profile, email, accounting.transactions]
    pkce: true

network:
  allowed_domains:
    - api.xero.com
    - identity.xero.com
    # No wildcards allowed — Security Agent enforces

operations:
  - id: create-invoice
    display_name: "Create Invoice"
    description: "Create a new invoice in Xero"
    input_schema:
      type: object
      required: [contact_id, line_items]
      properties:
        contact_id: { type: string }
        line_items: { type: array, items: { ... } }
    output_schema:
      type: object
      properties:
        invoice_id: { type: string }
        status: { type: string }
    errors:
      - code: CONTACT_NOT_FOUND
        description: "Contact ID does not exist in Xero"
    idempotency: header   # header | body_field | none
    rate_limit: 60        # calls per minute (for co-pilot pattern awareness)
```

JSON Schema for connector.yaml spec:
`packages/connector-sdk/src/schemas/connector-manifest.schema.json`

**Connector Entities (backend):**

`ConnectorDefinition`:
- Id (Guid), WorkspaceId (FK? — null for official/community connectors)
- ConnectorId (string — e.g. "xero-accounting"), PublisherId, DisplayName
- Version (string), Category, Tags (string[] jsonb)
- ManifestJson (jsonb — parsed connector.yaml)
- SourceUrl (string? — GitHub URL), Tier (enum: Official/Community/Verified/Marketplace)
- IsEnabled (bool), IsInstalled (bool per workspace via WorkspaceConnector)
- CreatedAt, UpdatedAt

`WorkspaceConnector` (connector installed into a workspace):
- Id (Guid), WorkspaceId (FK), ConnectorDefinitionId (FK)
- CredentialId (Guid? — FK to WorkspaceCredential)
- IsEnabled (bool), InstalledAt, InstalledBy (Guid)
- Index: (WorkspaceId + ConnectorDefinitionId) unique

`WorkspaceCredential` (encrypted credential vault):
- Id (Guid), WorkspaceId (FK), ConnectorDefinitionId (FK)
- Name (string — user-given alias), AuthType (string)
- EncryptedValue (bytea — AES-256-GCM encrypted), EncryptedKey (bytea — KMS-wrapped DEK)
- IV (bytea), Tag (bytea — GCM auth tag)
- ExpiresAt (DateTime?), LastUsedAt (DateTime?), IsActive (bool)
- CreatedBy (Guid), CreatedAt, UpdatedAt
- NEVER returned via API — only alias, expiry, isActive returned

Migration: `AddConnectorSchema`

**Credential Vault Service:**

`ICredentialVaultService` + `CredentialVaultService`:
- StoreAsync(workspaceId, connectorId, name, authType, plainValue, ct)
  → Guid credentialId
  AES-256-GCM encryption:
  1. Generate random 32-byte DEK per credential
  2. Wrap DEK with workspace master key (derived from CREDENTIAL_MASTER_KEY env var + workspaceId)
  3. Encrypt plainValue with DEK
  4. Store: EncryptedValue, EncryptedKey (wrapped DEK), IV, Tag
  5. Plain value NEVER stored, NEVER logged

- RetrieveAsync(workspaceId, credentialId, ct) → string plainValue
  1. Unwrap DEK using workspace master key
  2. Decrypt value using DEK + IV + Tag
  3. Returns plaintext — caller must handle securely

- RevokeAsync(workspaceId, credentialId, ct)
- ListAsync(workspaceId, ct) → List<CredentialAlias> (id, name, authType, expiresAt, isActive)
  NEVER returns EncryptedValue or decrypted value

Environment variable required:
CREDENTIAL_MASTER_KEY= (32-byte hex — must be set in all non-Development environments)

**Connector Sandbox (Flowamaz.Infrastructure/Connectors/ConnectorSandbox.cs):**

For Phase 4: execute connector operations via in-process isolated execution.
(Full container isolation is Year 2. Phase 4 uses process-level isolation.)

ConnectorSandbox.ExecuteAsync(workspaceId, connectorId, operationId, input, credentialId, ct):
1. Validate input against operation.input_schema (NJsonSchema)
2. Retrieve credential via ICredentialVaultService
3. Look up operation handler (TypeScript connector via Node.js subprocess? No — for Phase 4:
   built-in .NET HTTP connector handles HTTP-based connectors)
4. Execute HTTP call per connector manifest
5. Validate output against operation.output_schema
6. Return typed output or ConnectorExecutionError

For Phase 4: only HTTP-based connectors (REST APIs). The connector manifest describes
the HTTP call shape. The sandbox builds the HTTP request from the manifest.

**Official connector seed data (13 connectors from FUNCTIONAL.md §9.1):**
Seed all 13 official connector definitions on migration (no credentials — workspace-specific).

**Unit tests:**
- CredentialVaultService: store → retrieve roundtrip (AES-256 verified)
- CredentialVaultService: list never returns encrypted value
- CredentialVaultService: wrong workspace → cannot retrieve another workspace's credential
- ConnectorDefinition seed: 13 official connectors in DB after migration

## Technical Requirements
- [ ] AES-256-GCM encryption for all credential values
- [ ] CREDENTIAL_MASTER_KEY: startup fails in Production if not set
- [ ] Credential value: never in any log statement, never in any API response
- [ ] Workspace isolation: credential retrieval always verifies workspaceId
- [ ] ConnectorDefinition.ManifestJson validated against connector-manifest.schema.json on save
- [ ] 13 official connectors seeded with correct metadata

## Acceptance Criteria
- [ ] Store → Retrieve: same plaintext returned (AES roundtrip test)
- [ ] List credentials: EncryptedValue field does not exist in response
- [ ] Cross-workspace: workspace B cannot retrieve workspace A credential
- [ ] Migration: 13 official connector rows seeded
- [ ] CREDENTIAL_MASTER_KEY not set in Production → startup exception

## Output Expected
```
backend/Flowamaz.Core/Entities/Connector/ (ConnectorDefinition, WorkspaceConnector, WorkspaceCredential)
backend/Flowamaz.Core/Enums/ (ConnectorTier, AuthType)
backend/Flowamaz.Core/Interfaces/Services/ICredentialVaultService.cs
backend/Flowamaz.Application/Connectors/Services/ConnectorCatalogueService.cs
backend/Flowamaz.Infrastructure/Services/CredentialVaultService.cs
backend/Flowamaz.Infrastructure/Connectors/ConnectorSandbox.cs
backend/Flowamaz.Infrastructure/Persistence/Seed/ConnectorSeedData.cs
backend/Flowamaz.Infrastructure/Persistence/Migrations/[ts]_AddConnectorSchema.cs
backend/Flowamaz.Tests.Unit/Connectors/CredentialVaultServiceTests.cs
docs/connector-spec.md (connector.yaml format documented)
```
