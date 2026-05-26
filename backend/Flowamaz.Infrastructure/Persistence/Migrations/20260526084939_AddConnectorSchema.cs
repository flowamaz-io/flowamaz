using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "connector_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    publisher_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tags = table.Column<string>(type: "jsonb", nullable: false),
                    manifest_json = table.Column<string>(type: "jsonb", nullable: false),
                    source_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_installed = table.Column<bool>(type: "boolean", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connector_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workspace_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    auth_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    encrypted_value = table.Column<byte[]>(type: "bytea", nullable: false),
                    encrypted_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    iv = table.Column<byte[]>(type: "bytea", nullable: false),
                    tag = table.Column<byte[]>(type: "bytea", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_workspace_credentials_connector_definitions_connector_defin~",
                        column: x => x.connector_definition_id,
                        principalTable: "connector_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workspace_connectors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    installed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    installed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_connectors", x => x.id);
                    table.ForeignKey(
                        name: "fk_workspace_connectors_connector_definitions_connector_defini~",
                        column: x => x.connector_definition_id,
                        principalTable: "connector_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workspace_connectors_workspace_credentials_credential_id",
                        column: x => x.credential_id,
                        principalTable: "workspace_credentials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "connector_definitions",
                columns: new[] { "id", "category", "connector_id", "created_at", "created_by", "deleted_at", "display_name", "is_deleted", "is_enabled", "is_installed", "manifest_json", "publisher_id", "source_url", "tags", "tier", "updated_at", "updated_by", "version", "workspace_id" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0001-000000000001"), "generic", "http-rest", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "HTTP/REST", false, true, false, "{\"id\":\"http-rest\",\"version\":\"1.0.0\",\"operations\":[]}", "flowamaz-io", null, "[\"http\",\"rest\",\"api\"]", "Official", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0", null },
                    { new Guid("00000000-0000-0000-0001-000000000002"), "database", "postgresql", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "PostgreSQL", false, true, false, "{\"id\":\"postgresql\",\"version\":\"1.0.0\",\"operations\":[]}", "flowamaz-io", null, "[\"database\",\"sql\",\"postgres\"]", "Official", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0", null },
                    { new Guid("00000000-0000-0000-0001-000000000003"), "database", "mysql", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "MySQL", false, true, false, "{\"id\":\"mysql\",\"version\":\"1.0.0\",\"operations\":[]}", "flowamaz-io", null, "[\"database\",\"sql\",\"mysql\"]", "Official", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0", null },
                    { new Guid("00000000-0000-0000-0001-000000000004"), "messaging", "slack", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Slack", false, true, false, "{\"id\":\"slack\",\"version\":\"1.0.0\",\"operations\":[]}", "flowamaz-io", null, "[\"messaging\",\"slack\",\"notifications\"]", "Official", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0", null },
                    { new Guid("00000000-0000-0000-0001-000000000005"), "messaging", "microsoft-teams", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Microsoft Teams", false, true, false, "{\"id\":\"microsoft-teams\",\"version\":\"1.0.0\",\"operations\":[]}", "flowamaz-io", null, "[\"messaging\",\"teams\",\"microsoft\"]", "Official", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0", null },
                    { new Guid("00000000-0000-0000-0001-000000000006"), "messaging", "email-smtp", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Email (SMTP/Resend)", false, true, false, "{\"id\":\"email-smtp\",\"version\":\"1.0.0\",\"operations\":[]}", "flowamaz-io", null, "[\"email\",\"smtp\",\"resend\"]", "Official", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0", null },
                    { new Guid("00000000-0000-0000-0001-000000000007"), "generic", "webhook-emit", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Webhook Emit", false, true, false, "{\"id\":\"webhook-emit\",\"version\":\"1.0.0\",\"operations\":[]}", "flowamaz-io", null, "[\"webhook\",\"http\"]", "Official", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0", null },
                    { new Guid("00000000-0000-0000-0001-000000000008"), "generic", "webhook-receive", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Webhook Receive", false, true, false, "{\"id\":\"webhook-receive\",\"version\":\"1.0.0\",\"operations\":[]}", "flowamaz-io", null, "[\"webhook\",\"trigger\"]", "Official", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0", null },
                    { new Guid("00000000-0000-0000-0001-000000000009"), "generic", "file-system", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "File System", false, true, false, "{\"id\":\"file-system\",\"version\":\"1.0.0\",\"operations\":[]}", "flowamaz-io", null, "[\"files\",\"filesystem\",\"local\"]", "Official", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0", null },
                    { new Guid("00000000-0000-0000-0001-000000000010"), "generic", "schedule-cron", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Schedule/Cron", false, true, false, "{\"id\":\"schedule-cron\",\"version\":\"1.0.0\",\"operations\":[]}", "flowamaz-io", null, "[\"schedule\",\"cron\",\"timer\"]", "Official", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0", null },
                    { new Guid("00000000-0000-0000-0001-000000000011"), "generic", "script-shell", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Script/Shell", false, true, false, "{\"id\":\"script-shell\",\"version\":\"1.0.0\",\"operations\":[]}", "flowamaz-io", null, "[\"script\",\"shell\",\"bash\"]", "Official", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0", null },
                    { new Guid("00000000-0000-0000-0001-000000000012"), "devops", "github", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "GitHub", false, true, false, "{\"id\":\"github\",\"version\":\"1.0.0\",\"operations\":[]}", "flowamaz-io", null, "[\"github\",\"devops\",\"git\"]", "Official", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0", null },
                    { new Guid("00000000-0000-0000-0001-000000000013"), "productivity", "microsoft-365", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Microsoft 365", false, true, false, "{\"id\":\"microsoft-365\",\"version\":\"1.0.0\",\"operations\":[]}", "flowamaz-io", null, "[\"microsoft\",\"office365\",\"sharepoint\"]", "Official", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0", null }
                });

            migrationBuilder.CreateIndex(
                name: "ix_connector_definitions_category",
                table: "connector_definitions",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_connector_definitions_connector_id",
                table: "connector_definitions",
                column: "connector_id");

            migrationBuilder.CreateIndex(
                name: "ix_connector_definitions_tier",
                table: "connector_definitions",
                column: "tier");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_connectors_connector_definition_id",
                table: "workspace_connectors",
                column: "connector_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_connectors_credential_id",
                table: "workspace_connectors",
                column: "credential_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_connectors_workspace_id",
                table: "workspace_connectors",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_connectors_workspace_id_connector_definition_id",
                table: "workspace_connectors",
                columns: new[] { "workspace_id", "connector_definition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspace_credentials_connector_definition_id",
                table: "workspace_credentials",
                column: "connector_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_credentials_workspace_id",
                table: "workspace_credentials",
                column: "workspace_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workspace_connectors");

            migrationBuilder.DropTable(
                name: "workspace_credentials");

            migrationBuilder.DropTable(
                name: "connector_definitions");
        }
    }
}
