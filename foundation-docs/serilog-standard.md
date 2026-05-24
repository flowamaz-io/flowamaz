# Serilog Logging Standard

Every .NET project built under ClaudeCode Foundation follows this logging standard.
Verifier enforces compliance on every prompt output.

---

## Core Principle

Every function logs entry, exit and errors. No exceptions. No silent functions.
Logs must tell the complete story of what happened without reading the code.

---

## Log Levels

| Level   | When to Use                                                        |
|---------|--------------------------------------------------------------------|
| Info    | Normal operation — function entry, exit, significant state changes |
| Warning | Handled exceptions, unexpected but recoverable situations          |
| Error   | Failures, unhandled exceptions, data integrity issues              |

Debug level is not used in production. Verbose level is not used.

---

## Structured Logging

All logs must be structured JSON — not plain text strings.
This ensures CloudWatch and any log aggregator can filter and query effectively.

### Serilog Configuration (appsettings.json)

```json
{
  "Serilog": {
    "Using": ["Serilog.Sinks.Console", "Serilog.Sinks.File"],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "formatter": "Serilog.Formatting.Json.JsonFormatter"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/app-.log",
          "rollingInterval": "Day",
          "formatter": "Serilog.Formatting.Json.JsonFormatter"
        }
      }
    ],
    "Enrich": ["FromLogContext", "WithMachineName", "WithThreadId"]
  }
}
```

---

## Required Log Properties

Every log entry must include these properties as structured fields:

| Property      | Description                                      |
|---------------|--------------------------------------------------|
| CorrelationId | Request correlation ID for tracing across calls  |
| UserId        | Authenticated user ID (if available)             |
| FunctionName  | Name of the function logging                     |
| Layer         | Controller / Service / Repository                |
| Timestamp     | Auto-added by Serilog                            |

---

## Entry Log Pattern

Every function logs when it starts, including key input parameters.
Do not log sensitive data (passwords, tokens, card numbers).

```csharp
_logger.LogInformation(
    "Entering {FunctionName} with parameters: {@Parameters}",
    nameof(GetUserByIdAsync),
    new { userId, includeRoles }
);
```

---

## Exit Log Pattern

Every function logs when it completes, including result summary.
Do not log full objects if they contain sensitive data.

```csharp
_logger.LogInformation(
    "Exiting {FunctionName} — Result: {@Result}",
    nameof(GetUserByIdAsync),
    new { found = user != null, userId }
);
```

---

## Error Log Pattern

All exceptions are logged with full context before being handled or rethrown.

```csharp
_logger.LogError(
    ex,
    "Error in {FunctionName} — {ErrorMessage} — Parameters: {@Parameters}",
    nameof(GetUserByIdAsync),
    ex.Message,
    new { userId }
);
```

---

## What Must Never Be Logged

- Passwords or password hashes
- JWT tokens or refresh tokens
- API keys or secrets
- Credit card numbers or financial data
- Full PII beyond user ID (no email addresses, phone numbers in logs)
- Database connection strings

---

## Verifier Compliance Checklist

Verifier flags the following as issues:

- [ ] Any function missing entry log
- [ ] Any function missing exit log
- [ ] Any exception caught without error log
- [ ] Plain text string logs instead of structured properties
- [ ] Sensitive data present in any log statement
- [ ] Missing CorrelationId in any log entry
- [ ] Console.WriteLine used instead of Serilog
- [ ] Debug or Verbose log level used
