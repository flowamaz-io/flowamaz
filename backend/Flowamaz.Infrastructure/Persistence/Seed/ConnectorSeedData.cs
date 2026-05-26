using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Enums;

namespace Flowamaz.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotent seed data for the 13 official platform connectors.
/// Applied via HasData in OnModelCreating — the migration is the seed.
/// Fixed Guid IDs and seed timestamp ensure deterministic migrations.
/// </summary>
internal static class ConnectorSeedData
{
    private static readonly DateTime SeedTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static IReadOnlyList<ConnectorDefinition> OfficialConnectors { get; } =
    [
        Connector(
            id:          "00000000-0000-0000-0001-000000000001",
            connectorId: "http-rest",
            displayName: "HTTP/REST",
            category:    "generic",
            tags:        ["http", "rest", "api"]),

        Connector(
            id:          "00000000-0000-0000-0001-000000000002",
            connectorId: "postgresql",
            displayName: "PostgreSQL",
            category:    "database",
            tags:        ["database", "sql", "postgres"]),

        Connector(
            id:          "00000000-0000-0000-0001-000000000003",
            connectorId: "mysql",
            displayName: "MySQL",
            category:    "database",
            tags:        ["database", "sql", "mysql"]),

        Connector(
            id:          "00000000-0000-0000-0001-000000000004",
            connectorId: "slack",
            displayName: "Slack",
            category:    "messaging",
            tags:        ["messaging", "slack", "notifications"]),

        Connector(
            id:          "00000000-0000-0000-0001-000000000005",
            connectorId: "microsoft-teams",
            displayName: "Microsoft Teams",
            category:    "messaging",
            tags:        ["messaging", "teams", "microsoft"]),

        Connector(
            id:          "00000000-0000-0000-0001-000000000006",
            connectorId: "email-smtp",
            displayName: "Email (SMTP/Resend)",
            category:    "messaging",
            tags:        ["email", "smtp", "resend"]),

        Connector(
            id:          "00000000-0000-0000-0001-000000000007",
            connectorId: "webhook-emit",
            displayName: "Webhook Emit",
            category:    "generic",
            tags:        ["webhook", "http"]),

        Connector(
            id:          "00000000-0000-0000-0001-000000000008",
            connectorId: "webhook-receive",
            displayName: "Webhook Receive",
            category:    "generic",
            tags:        ["webhook", "trigger"]),

        Connector(
            id:          "00000000-0000-0000-0001-000000000009",
            connectorId: "file-system",
            displayName: "File System",
            category:    "generic",
            tags:        ["files", "filesystem", "local"]),

        Connector(
            id:          "00000000-0000-0000-0001-000000000010",
            connectorId: "schedule-cron",
            displayName: "Schedule/Cron",
            category:    "generic",
            tags:        ["schedule", "cron", "timer"]),

        Connector(
            id:          "00000000-0000-0000-0001-000000000011",
            connectorId: "script-shell",
            displayName: "Script/Shell",
            category:    "generic",
            tags:        ["script", "shell", "bash"]),

        Connector(
            id:          "00000000-0000-0000-0001-000000000012",
            connectorId: "github",
            displayName: "GitHub",
            category:    "devops",
            tags:        ["github", "devops", "git"]),

        Connector(
            id:          "00000000-0000-0000-0001-000000000013",
            connectorId: "microsoft-365",
            displayName: "Microsoft 365",
            category:    "productivity",
            tags:        ["microsoft", "office365", "sharepoint"]),
    ];

    private static ConnectorDefinition Connector(
        string id,
        string connectorId,
        string displayName,
        string category,
        string[] tags) => new()
    {
        Id            = Guid.Parse(id),
        ConnectorId   = connectorId,
        PublisherId   = "flowamaz-io",
        DisplayName   = displayName,
        Version       = "1.0.0",
        Category      = category,
        Tags          = tags,
        ManifestJson  = $$$"""{"id":"{{{connectorId}}}","version":"1.0.0","operations":[]}""",
        SourceUrl     = null,
        Tier          = ConnectorTier.Official,
        IsEnabled     = true,
        IsInstalled   = false,
        WorkspaceId   = null,
        CreatedAt     = SeedTimestamp,
        UpdatedAt     = SeedTimestamp,
        IsDeleted     = false,
        DeletedAt     = null,
        CreatedBy     = null,
        UpdatedBy     = null
    };
}
