# Connector Specification — connector.yaml Format

This document describes the `connector.yaml` manifest format used by Flowamaz connectors.

---

## Overview

A connector manifest defines the connector's identity, authentication requirements, and the operations it exposes. Manifests are stored as `ManifestJson` (jsonb) in the `connector_definitions` table.

---

## Full Example

```yaml
id: my-connector
version: "1.0.0"
publisher: my-org
display_name: My Service
category: generic
description: Integrates with My Service API.

auth:
  type: ApiKey          # One of: ApiKey, OAuth2AuthCode, OAuth2ClientCredentials,
                        #         Basic, Custom, ConnectionString, HmacSecret,
                        #         None, BearerToken, ByomEndpoint
  label: "API Key"
  help_url: https://docs.myservice.com/api-keys

operations:
  - id: get_item
    display_name: Get Item
    description: Fetches a single item by ID.
    http:
      method: GET
      url: https://api.myservice.com/items/{id}
      headers:
        X-Custom-Header: "flowamaz"
    input_schema:
      type: object
      required: [id]
      properties:
        id:
          type: string
          description: The item identifier.
    output_schema:
      type: object
      properties:
        id:
          type: string
        name:
          type: string
        created_at:
          type: string
          format: date-time

  - id: create_item
    display_name: Create Item
    description: Creates a new item.
    http:
      method: POST
      url: https://api.myservice.com/items
    input_schema:
      type: object
      required: [name]
      properties:
        name:
          type: string
    output_schema:
      type: object
      properties:
        id:
          type: string
```

---

## Top-Level Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Unique slug, e.g. `http-rest`. Matches `ConnectorDefinition.ConnectorId`. |
| `version` | string | Yes | SemVer, e.g. `1.0.0`. |
| `publisher` | string | Yes | Publisher slug, e.g. `flowamaz-io`. |
| `display_name` | string | Yes | Human-readable name shown in UI. |
| `category` | string | Yes | One of: `generic`, `database`, `messaging`, `devops`, `productivity`. |
| `description` | string | No | Short description of the connector. |
| `auth` | object | No | Authentication configuration (omit for `None` auth type). |
| `operations` | array | Yes | List of operations this connector exposes. |

---

## Auth Object

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | string | Yes | Auth type (see `ConnectorAuthType` enum). |
| `label` | string | No | Label shown in credential form. |
| `help_url` | string | No | URL to documentation on obtaining credentials. |

### Supported Auth Types

| Value | Description |
|-------|-------------|
| `None` | No authentication required. |
| `ApiKey` | Single API key header/query param. |
| `BearerToken` | Bearer token in Authorization header. |
| `Basic` | HTTP Basic Auth (username + password). |
| `OAuth2AuthCode` | OAuth 2.0 Authorization Code flow. |
| `OAuth2ClientCredentials` | OAuth 2.0 Client Credentials flow. |
| `HmacSecret` | HMAC-SHA256 signature. |
| `ConnectionString` | Database or service connection string. |
| `Custom` | Custom multi-field credential. |
| `ByomEndpoint` | Bring Your Own Model — endpoint + key. |

---

## Operation Object

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Unique operation slug within the connector. |
| `display_name` | string | Yes | Human-readable operation name. |
| `description` | string | No | What this operation does. |
| `http` | object | Yes (Phase 4) | HTTP execution configuration. |
| `input_schema` | JSON Schema | No | JSON Schema for validating input. Validation is strict. |
| `output_schema` | JSON Schema | No | JSON Schema for validating output. Validation is strict. |

### HTTP Object

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `method` | string | Yes | HTTP method: `GET`, `POST`, `PUT`, `PATCH`, `DELETE`. |
| `url` | string | Yes | Endpoint URL. Supports `{variable}` substitution. |
| `headers` | object | No | Static headers to include in every request. |

---

## Schema Validation

- `input_schema` is validated before the HTTP call using NJsonSchema.
- `output_schema` is validated after a successful HTTP response.
- If no schema is provided, validation is skipped (all values pass).
- Validation errors return a `ConnectorExecutionResult` with `Success=false`.

---

## Official Connectors

The 13 official connectors seeded by `ConnectorSeedData` are:

| ConnectorId | Display Name | Category |
|-------------|--------------|----------|
| `http-rest` | HTTP/REST | generic |
| `postgresql` | PostgreSQL | database |
| `mysql` | MySQL | database |
| `slack` | Slack | messaging |
| `microsoft-teams` | Microsoft Teams | messaging |
| `email-smtp` | Email (SMTP/Resend) | messaging |
| `webhook-emit` | Webhook Emit | generic |
| `webhook-receive` | Webhook Receive | generic |
| `file-system` | File System | generic |
| `schedule-cron` | Schedule/Cron | generic |
| `script-shell` | Script/Shell | generic |
| `github` | GitHub | devops |
| `microsoft-365` | Microsoft 365 | productivity |
