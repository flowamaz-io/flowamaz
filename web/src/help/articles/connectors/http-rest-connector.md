---
title: HTTP / REST Connector
updated: 2026-05-30
readingTime: "4 min read"
plans: "All plans"
---

# HTTP / REST Connector

Alongside the 13 official connectors, Flowamaz ships a generic **HTTP / REST**
connector. Use it to call any API that doesn't have a dedicated connector yet — your
own services, niche SaaS tools, or internal endpoints.

## When to use it

Reach for a dedicated connector first: they give you typed operations and managed auth.
Use the HTTP / REST connector when no official connector exists for the service you
need to call.

## Configuration reference

Add an **Action** node and select the HTTP / REST connector. Then configure the
request:

| Field | Description |
|-------|-------------|
| **Method** | `GET`, `POST`, `PUT`, `PATCH`, `DELETE`. |
| **URL** | Full endpoint URL. Supports `{{variable}}` expressions. |
| **Headers** | Key/value pairs sent with the request. |
| **Query params** | Appended to the URL as the query string. |
| **Body** | Request body (usually JSON) for write methods. |
| **Auth** | The vault credential to attach (see below). |

Any field accepts `{{...}}` expressions, so you can build URLs and bodies from earlier
node outputs.

## Authentication

Credentials are stored in the workspace vault — you select a credential alias, never a
raw secret, and the value is never shown again after you save it. Supported types:

- **API key** — sent as a header or query param.
- **Bearer token** — added as `Authorization: Bearer ...`.
- **Basic auth** — username and password.
- **OAuth** — for APIs that support an OAuth flow.

## Example

```yaml
- id: create-ticket
  type: Action
  config:
    connector: http-rest
    method: POST
    url: "https://api.example.com/v1/tickets"
    headers:
      Content-Type: application/json
    credential: example-api-key
    body:
      subject: "{{input.subject}}"
      priority: high
```

## Reading the response

The node outputs the response **status**, **headers**, and parsed **body**. Reference
them downstream, for example `{{create-ticket.body.id}}`. Non-2xx responses surface as
node errors — wrap the call in a `TryCatch` node if you want to handle failures
gracefully instead of failing the instance.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/connectors/http-rest-connector.md)
