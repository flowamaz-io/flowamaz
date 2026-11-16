# Flowamaz.Engine.Api

HTTP endpoints for the Flowamaz workflow runtime in an ASP.NET Core host (MIT).

## Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFlowamazEngineRuntime(builder.Configuration);        // Flowamaz.Engine.Hosting
builder.Services.AddFlowamazEnginePersistence(o => o.UseNpgsql(           // Flowamaz.Engine.Persistence
    builder.Configuration.GetConnectionString("Engine"), EngineDbContextOptions.ConfigureNpgsql));
builder.Services.AddFlowamazEngineWorker();
builder.Services.AddFlowamazEngineApi(builder.Configuration);

// Your sign-in, and your rule for who may do what (required):
builder.Services.AddAuthentication(/* … */);
builder.Services.AddSingleton<IEngineApiAccess>(new SignedInUserAccess(workspaceId));

var app = builder.Build();
app.UseAuthentication();
app.MapFlowamazEngineApi("/api/v1");
app.Run();
```

**Access is always the host's decision.** `MapFlowamazEngineApi` refuses to start without an `IEngineApiAccess`. Every workspace route calls it with the action:

- `Read`, `Author`, `Publish`, `Run`, `Operate` (cancel, retry) or `Decide` (gates).
- It returns the caller (the user id that's recorded, plus what they may see of runs) or null.
- Null gives 401 when the request isn't signed in, and 403 when it is.

`SignedInUserAccess` is the simplest rule: any signed-in user may do everything in one workspace. The user id comes from the `sub` claim, and it must be a GUID. For roles, write your own rule.

## Endpoints

Routes start with the prefix (`/api/v1` here). JSON is camelCase, with enums as strings. Errors come back as `{ "code", "message" }`, with their HTTP status.

| Method and route | Action | |
|---|---|---|
| `GET /workspaces/{ws}/workflows?page&pageSize` | Read | `{ items, total, page, pageSize }` |
| `POST /workspaces/{ws}/workflows` | Author | `{ name, slug, yamlContent, description?, slaThresholdMs? }` → 201 |
| `GET /workspaces/{ws}/workflows/{id}` | Read | |
| `PUT /workspaces/{ws}/workflows/{id}` | Author | `{ yamlContent, slaThresholdMs? }`: save the draft |
| `DELETE /workspaces/{ws}/workflows/{id}` | Author | drafts only (published ones are archived) → 204 |
| `POST /workspaces/{ws}/workflows/{id}/publish` | Publish | validates the YAML, makes a new production version |
| `POST /workspaces/{ws}/workflows/{id}/archive`, `/unarchive` | Publish | |
| `GET /workspaces/{ws}/workflows/{id}/versions` | Read | |
| `POST /workspaces/{ws}/workflows/{id}/runs` | Run | `{ payload?, isTest? }`, header `Idempotency-Key` optional → 202 `{ instanceId, status, triggeredAt }` |
| `GET /workspaces/{ws}/runs?status&workflowId&includeTest&page&pageSize` | Read | newest first |
| `GET /workspaces/{ws}/runs/{id}`, `/events`, `/timeline` | Read | variables marked sensitive are masked |
| `POST /workspaces/{ws}/runs/{id}/cancel` | Operate | |
| `POST /workspaces/{ws}/runs/{id}/retry` | Operate | a new run on the same pinned version → 202 |
| `GET /workspaces/{ws}/gates?includeTest` | Read | gates waiting for a decision |
| `GET /workspaces/{ws}/gates/{runId}/{nodeId}` | Read | |
| `POST /workspaces/{ws}/gates/{runId}/{nodeId}/decide` | Decide | `{ decision: "approved" \| "rejected", note?, values? }` |
| `POST /hooks/workspaces/{ws}/workflows/{id}` | (signature) | the webhook trigger, below |

## Webhook trigger

The webhook route needs no sign-in. Instead, the request must carry `X-Flowamaz-Signature: sha256=<hex HMAC-SHA256 of the raw body>`, keyed with the webhook secret.

- **The body:** a JSON object, which becomes the run's trigger payload.
- **Repeats:** an `Idempotency-Key` header makes a repeat delivery return the same run.
- **Responses:** 202 with the run id; 401 for a missing or wrong signature; 400 for a body that isn't JSON; 404 when webhooks are off.
- **The secret:** `EngineApi:WebhookSecret`, at least 32 characters, kept in a secret store. It's used for every workflow; without it, webhooks are off. To give each workflow its own secret, register an `IWebhookSecretResolver`.

Settings (section `EngineApi`):

- `WebhookSecret`
- `MaxWebhookBodyBytes` (default 1 MB)
- `MaxPageSize` (default 100)

`Flowamaz.Engine.Runtime.Tests/Api/EngineApiEndpointTests.cs` is a working example on ASP.NET Core's test server.
