using System.Text.Json;

namespace Flowamaz.Application.Connectors;

/// <summary>One operation of an official connector, as published in its manifest.</summary>
/// <param name="Internal">Used by the platform itself (e.g. gate approval cards); hidden from the node config UI.</param>
public sealed record ConnectorOperationSpec(
    string Id,
    string Name,
    string Description,
    bool RequiresCredential,
    IReadOnlyList<string> Required,
    IReadOnlyDictionary<string, (string Type, string Description)> Properties,
    bool Internal = false);

public sealed record ConnectorSpec(
    string ConnectorId,
    string AuthType,
    IReadOnlyList<string> CredentialTypes,
    IReadOnlyList<ConnectorOperationSpec> Operations,
    IReadOnlyList<string>? Triggers = null);

/// <summary>
/// Single source of truth for the official connectors' operations. Drives the seeded manifests
/// (what the node-config UI lists), input validation before a handler runs, and whether a
/// credential is required. A handler must exist for every non-trigger operation listed here
/// (enforced by a unit test).
/// </summary>
public static class OfficialConnectorCatalog
{
    private static (string, string) S(string d) => ("string", d);
    private static (string, string) O(string d) => ("object", d);
    private static (string, string) A(string d) => ("array", d);
    private static (string, string) N(string d) => ("integer", d);
    private static (string, string) B(string d) => ("boolean", d);

    private static Dictionary<string, (string Type, string Description)> P(params (string Key, (string, string) Spec)[] props) =>
        props.ToDictionary(p => p.Key, p => p.Spec);

    private static readonly Dictionary<string, (string Type, string Description)> HttpProps = P(
        ("url", S("Full https URL to call")),
        ("headers", O("Extra request headers")),
        ("body", S("Request body (JSON text)")),
        ("timeout_seconds", N("Timeout in seconds (default 30, max 120)")),
        ("api_key_header", S("Header for an ApiKey credential (default X-API-Key)")));

    private static ConnectorOperationSpec Http(string verb) =>
        new(verb, $"HTTP {verb}", $"Send an HTTP {verb} request", false, ["url"], HttpProps);

    private static readonly Dictionary<string, (string Type, string Description)> SqlProps = P(
        ("sql", S("SQL with @named parameters — never concatenate values into the SQL")),
        ("parameters", O("Parameter values by name")));

    public static IReadOnlyList<ConnectorSpec> Connectors { get; } =
    [
        new("http-rest", "optional", ["BearerToken", "ApiKey", "BasicAuth"],
            [Http("GET"), Http("POST"), Http("PUT"), Http("PATCH"), Http("DELETE"), Http("HEAD")]),

        new("postgresql", "connection-string", ["ConnectionString"],
        [
            new("query", "Run query", "Run a SELECT and return rows", true, ["sql"], SqlProps),
            new("execute", "Execute statement", "Run INSERT/UPDATE/DELETE and return affected rows", true, ["sql"], SqlProps),
        ]),

        new("mysql", "connection-string", ["ConnectionString"],
        [
            new("query", "Run query", "Run a SELECT and return rows", true, ["sql"], SqlProps),
            new("execute", "Execute statement", "Run INSERT/UPDATE/DELETE and return affected rows", true, ["sql"], SqlProps),
        ]),

        new("slack", "oauth2-auth-code", ["OAuth2AuthCode", "BearerToken"],
        [
            new("send-message", "Send message", "Post a message to a channel", true, ["channel", "text"],
                P(("channel", S("Channel name or id, e.g. #approvals")), ("text", S("Message text")), ("blocks", A("Optional Block Kit blocks")))),
            new("send-dm", "Send direct message", "Send a direct message to a user", true, ["user_id", "text"],
                P(("user_id", S("Slack user id, e.g. U0123")), ("text", S("Message text")))),
            new("post-approval-message", "Post approval message", "Approval card for a human gate", true,
                ["channel", "gate_decision_id"],
                P(("channel", S("Channel")), ("gate_decision_id", S("Gate id")), ("workflow_name", S("Workflow")), ("gate_summary", S("Summary"))),
                Internal: true),
        ]),

        new("microsoft-teams", "oauth2-auth-code", ["OAuth2AuthCode"],
        [
            new("send-message", "Send channel message", "Post a message to a Teams channel (Microsoft Graph)", true,
                ["team_id", "channel_id", "text"],
                P(("team_id", S("Team id")), ("channel_id", S("Channel id")), ("text", S("Message text (HTML allowed)")))),
            new("send-webhook-message", "Send via incoming webhook", "Post to a channel through an incoming-webhook URL", false,
                ["webhook_url", "text"],
                P(("webhook_url", S("Incoming webhook URL")), ("text", S("Message text")))),
            new("post-adaptive-card", "Post approval card", "Adaptive Card for a human gate", false,
                ["webhook_url", "gate_id"],
                P(("webhook_url", S("Webhook")), ("gate_id", S("Gate id")), ("gate_summary", S("Summary")), ("platform_url", S("Platform URL"))),
                Internal: true),
        ]),

        new("email-smtp", "platform", [],
        [
            new("send-email", "Send email", "Send a transactional email from the platform sender", false, ["to", "subject", "body"],
                P(("to", S("Recipient address")), ("subject", S("Subject")), ("body", S("Body")), ("is_html", B("Body is HTML (default false)")))),
        ]),

        new("webhook-emit", "hmac", ["HmacSecret"],
        [
            new("send-webhook", "Send webhook", "POST a JSON payload, HMAC-signed when a credential is set", false, ["url", "payload"],
                P(("url", S("Destination https URL")), ("payload", O("JSON payload")))),
        ]),

        new("webhook-receive", "hmac", ["HmacSecret"], [], Triggers: ["webhook"]),

        new("file-system", "none", [],
        [
            new("read-file", "Read file", "Read a text file from the workspace file area", false, ["path"],
                P(("path", S("Path relative to the workspace file area")))),
            new("write-file", "Write file", "Write a text file to the workspace file area", false, ["path", "content"],
                P(("path", S("Path relative to the workspace file area")), ("content", S("Text content")), ("overwrite", B("Replace an existing file (default false)")))),
            new("list-files", "List files", "List files in a folder of the workspace file area", false, [],
                P(("path", S("Folder (default: root)")))),
        ]),

        new("schedule-cron", "none", [],
        [
            new("validate-cron", "Validate cron", "Check a cron expression and return its next run times", false, ["expression"],
                P(("expression", S("Cron expression (5 or 6 fields)")), ("timezone", S("IANA time zone (default UTC)")))),
        ], Triggers: ["schedule"]),

        new("script-shell", "none", [],
        [
            new("run-script", "Run script", "Run a bash or python script (self-hosted only; disabled on Cloud)", false, ["interpreter", "script"],
                P(("interpreter", S("bash or python3")), ("script", S("Script text (passed on stdin, never through a shell string)")),
                  ("args", A("Arguments")), ("timeout_seconds", N("Timeout (default 30, max 300)")))),
        ]),

        new("github", "oauth2-auth-code", ["OAuth2AuthCode", "BearerToken"],
        [
            new("create-issue", "Create issue", "Open an issue", true, ["owner", "repo", "title"],
                P(("owner", S("Owner")), ("repo", S("Repository")), ("title", S("Title")), ("body", S("Body")), ("labels", A("Labels")))),
            new("get-issue", "Get issue", "Fetch an issue", true, ["owner", "repo", "issue_number"],
                P(("owner", S("Owner")), ("repo", S("Repository")), ("issue_number", N("Issue number")))),
            new("create-pr", "Create pull request", "Open a pull request", true, ["owner", "repo", "title", "head", "base"],
                P(("owner", S("Owner")), ("repo", S("Repository")), ("title", S("Title")), ("head", S("Source branch")), ("base", S("Target branch")), ("body", S("Description")))),
            new("add-comment", "Add comment", "Comment on an issue or pull request", true, ["owner", "repo", "issue_number", "body"],
                P(("owner", S("Owner")), ("repo", S("Repository")), ("issue_number", N("Issue or PR number")), ("body", S("Comment")))),
        ]),

        new("microsoft-365", "oauth2-auth-code", ["OAuth2AuthCode"],
        [
            new("send-mail", "Send email", "Send email from the connected mailbox (Microsoft Graph)", true, ["to", "subject", "body"],
                P(("to", S("Recipients, comma-separated")), ("subject", S("Subject")), ("body", S("Body")), ("is_html", B("Body is HTML (default false)")))),
            new("create-calendar-event", "Create calendar event", "Create an event in the connected calendar", true, ["subject", "start", "end"],
                P(("subject", S("Title")), ("start", S("Start, ISO 8601")), ("end", S("End, ISO 8601")), ("timezone", S("IANA or Windows time zone (default UTC)")),
                  ("attendees", S("Attendee emails, comma-separated")), ("body", S("Description")))),
            new("upload-file", "Upload file to OneDrive", "Create or replace a text file in the connected OneDrive", true, ["path", "content"],
                P(("path", S("OneDrive path, e.g. Reports/summary.txt")), ("content", S("Text content")))),
        ]),
    ];

    private static readonly Dictionary<(string, string), ConnectorOperationSpec> ByKey =
        Connectors.SelectMany(c => c.Operations.Select(o => (c.ConnectorId, o)))
            .ToDictionary(x => (x.ConnectorId, x.o.Id), x => x.o);

    public static ConnectorOperationSpec? Find(string connectorId, string operationId) =>
        ByKey.GetValueOrDefault((connectorId, operationId));

    public static ConnectorSpec? FindConnector(string connectorId) =>
        Connectors.FirstOrDefault(c => c.ConnectorId == connectorId);

    /// <summary>Manifest JSON stored on the seeded ConnectorDefinition (deterministic for EF HasData).</summary>
    public static string ManifestJson(string connectorId)
    {
        var spec = FindConnector(connectorId);
        var manifest = new Dictionary<string, object?>
        {
            ["id"] = connectorId,
            ["version"] = "1.1.0",
            ["auth"] = new { type = spec?.AuthType ?? "none", credential_types = spec?.CredentialTypes ?? [] },
            ["triggers"] = spec?.Triggers ?? [],
            ["operations"] = (spec?.Operations ?? []).Select(o => new Dictionary<string, object?>
            {
                ["id"] = o.Id,
                ["name"] = o.Name,
                ["description"] = o.Description,
                ["requires_credential"] = o.RequiresCredential,
                ["internal"] = o.Internal,
                ["input_schema"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["required"] = o.Required,
                    ["properties"] = o.Properties.ToDictionary(
                        p => p.Key, p => (object)new { type = p.Value.Type, description = p.Value.Description }),
                },
            }).ToList(),
        };
        return JsonSerializer.Serialize(manifest);
    }
}
