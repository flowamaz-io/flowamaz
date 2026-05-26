# AI Nodes

An **AI** node sends a prompt to a language model and stores the response in a workflow variable.

## What it does
Calls Claude (or another configured model) with a prompt you write. The model's response is stored so downstream nodes can use it. The model is resolved at runtime from the workspace AI configuration — you never hardcode a model name.

## Config fields
| Field | Required | Description |
|-------|----------|-------------|
| `prompt` | Yes | The user prompt sent to the model. Supports `{{variable}}` interpolation. |
| `systemPrompt` | No | Optional system prompt to set context or persona. |
| `outputVariable` | Yes | Variable name to store the AI response text. |
| `outputSchema` | No | JSON Schema to validate and parse the AI response into structured data. |
| `model` | No | Override the workspace model for this node (node-level override). |
| `maxTokens` | No | Maximum tokens to generate in the response. |
| `temperature` | No | Sampling temperature (0.0–1.0). Lower = more deterministic. |

## YAML Example
```yaml
- id: classify-ticket
  type: ai
  label: "Classify support ticket"
  config:
    prompt: "Classify this support ticket as urgent/normal/low: {{input.ticket_body}}"
    outputVariable: ticket_priority
    outputSchema:
      type: object
      properties:
        priority:
          type: string
          enum: [urgent, normal, low]
```

## Common mistakes
- **No outputVariable** — the response is lost if you don't name a variable to store it.
- **Hardcoded model IDs** — use the model resolution hierarchy instead. Hardcoded models bypass cost controls.
- **Missing outputSchema for structured data** — if you need a specific JSON shape, always define `outputSchema`. Without it, the response is treated as raw text.
