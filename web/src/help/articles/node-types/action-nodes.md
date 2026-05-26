# Action Nodes

An **Action** node makes an outbound HTTP call to an external service or connector.

## What it does
Sends a request to a URL and stores the response in a workflow variable. On failure, the retry policy applies before the workflow is marked as failed.

## Config fields
| Field | Required | Description |
|-------|----------|-------------|
| `url` | Yes | Target URL. Supports `{{variable}}` interpolation. |
| `method` | Yes | HTTP method: `GET`, `POST`, `PUT`, `PATCH`, `DELETE` |
| `headers` | No | Key-value map of request headers |
| `body` | No | Request body as a JSON object or string template |
| `outputVariable` | No | Variable name to store the response body |
| `retry` | No | Retry policy: `maxAttempts`, `backoffSeconds` |
| `timeout` | No | Request timeout in seconds (default: 30) |

## YAML Example
```yaml
- id: notify-slack
  type: action
  label: "Send Slack message"
  config:
    url: "https://hooks.slack.com/services/{{env.SLACK_WEBHOOK}}"
    method: POST
    body:
      text: "Workflow {{workflow.name}} completed"
    retry:
      maxAttempts: 3
      backoffSeconds: 5
```

## Common mistakes
- **Hardcoded secrets** — never put API keys in the `url` or `body`. Use credential references (`{{credentials.my-api-key}}`).
- **Missing outputVariable** — if you need the response later, set `outputVariable` or the data is discarded.
- **No retry on flaky APIs** — external APIs fail. Always add a retry policy for production workflows.
