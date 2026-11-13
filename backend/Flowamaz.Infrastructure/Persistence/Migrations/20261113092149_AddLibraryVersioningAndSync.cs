using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryVersioningAndSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "installed_version",
                table: "workspace_connectors",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "deprecated_at",
                table: "workflow_templates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deprecation_note",
                table: "workflow_templates",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "publisher_id",
                table: "workflow_templates",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "changelog_json",
                table: "connector_definitions",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<DateTime>(
                name: "deprecated_at",
                table: "connector_definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deprecation_note",
                table: "connector_definitions",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_latest_major",
                table: "connector_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "major_version",
                table: "connector_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "library_sync_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    repository = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    last_commit_sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_success_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    alerted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_library_sync_states", x => x.id);
                });

            // Existing rows: the major from their version, and what each workspace runs today.
            migrationBuilder.Sql(@"UPDATE connector_definitions SET major_version = split_part(version, '.', 1)::int WHERE version ~ '^[0-9]+\.[0-9]+\.[0-9]+';");
            migrationBuilder.Sql(@"UPDATE workspace_connectors wc SET installed_version = cd.version FROM connector_definitions cd WHERE cd.id = wc.connector_definition_id AND wc.installed_version = '';");

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000001"),
                columns: new[] { "changelog_json", "deprecated_at", "deprecation_note", "is_latest_major", "major_version" },
                values: new object[] { "[]", null, null, true, 1 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000002"),
                columns: new[] { "changelog_json", "deprecated_at", "deprecation_note", "is_latest_major", "major_version" },
                values: new object[] { "[]", null, null, true, 1 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000003"),
                columns: new[] { "changelog_json", "deprecated_at", "deprecation_note", "is_latest_major", "major_version" },
                values: new object[] { "[]", null, null, true, 1 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000004"),
                columns: new[] { "changelog_json", "deprecated_at", "deprecation_note", "is_latest_major", "major_version" },
                values: new object[] { "[]", null, null, true, 1 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000005"),
                columns: new[] { "changelog_json", "deprecated_at", "deprecation_note", "is_latest_major", "major_version" },
                values: new object[] { "[]", null, null, true, 1 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000006"),
                columns: new[] { "changelog_json", "deprecated_at", "deprecation_note", "is_latest_major", "major_version" },
                values: new object[] { "[]", null, null, true, 1 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000007"),
                columns: new[] { "changelog_json", "deprecated_at", "deprecation_note", "is_latest_major", "major_version" },
                values: new object[] { "[]", null, null, true, 1 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000008"),
                columns: new[] { "changelog_json", "deprecated_at", "deprecation_note", "is_latest_major", "major_version" },
                values: new object[] { "[]", null, null, true, 1 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000009"),
                columns: new[] { "changelog_json", "deprecated_at", "deprecation_note", "is_latest_major", "major_version" },
                values: new object[] { "[]", null, null, true, 1 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000010"),
                columns: new[] { "changelog_json", "deprecated_at", "deprecation_note", "is_latest_major", "major_version" },
                values: new object[] { "[]", null, null, true, 1 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000011"),
                columns: new[] { "changelog_json", "deprecated_at", "deprecation_note", "is_latest_major", "major_version" },
                values: new object[] { "[]", null, null, true, 1 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000012"),
                columns: new[] { "changelog_json", "deprecated_at", "deprecation_note", "is_latest_major", "major_version" },
                values: new object[] { "[]", null, null, true, 1 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000013"),
                columns: new[] { "changelog_json", "deprecated_at", "deprecation_note", "is_latest_major", "major_version" },
                values: new object[] { "[]", null, null, true, 1 });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000001"),
                columns: new[] { "deprecated_at", "deprecation_note", "publisher_id" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000002"),
                columns: new[] { "deprecated_at", "deprecation_note", "publisher_id" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000003"),
                columns: new[] { "deprecated_at", "deprecation_note", "publisher_id" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000004"),
                columns: new[] { "deprecated_at", "deprecation_note", "publisher_id" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000005"),
                columns: new[] { "deprecated_at", "deprecation_note", "publisher_id" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000006"),
                columns: new[] { "deprecated_at", "deprecation_note", "publisher_id" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000007"),
                columns: new[] { "deprecated_at", "deprecation_note", "publisher_id" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000008"),
                columns: new[] { "deprecated_at", "deprecation_note", "publisher_id" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000009"),
                columns: new[] { "deprecated_at", "deprecation_note", "publisher_id" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000010"),
                columns: new[] { "deprecated_at", "deprecation_note", "publisher_id" },
                values: new object[] { null, null, null });

            migrationBuilder.CreateIndex(
                name: "ix_connector_definitions_publisher_id_connector_id_major_versi~",
                table: "connector_definitions",
                columns: new[] { "publisher_id", "connector_id", "major_version" });

            migrationBuilder.CreateIndex(
                name: "ix_library_sync_states_source",
                table: "library_sync_states",
                column: "source",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "library_sync_states");

            migrationBuilder.DropIndex(
                name: "ix_connector_definitions_publisher_id_connector_id_major_versi~",
                table: "connector_definitions");

            migrationBuilder.DropColumn(
                name: "installed_version",
                table: "workspace_connectors");

            migrationBuilder.DropColumn(
                name: "deprecated_at",
                table: "workflow_templates");

            migrationBuilder.DropColumn(
                name: "deprecation_note",
                table: "workflow_templates");

            migrationBuilder.DropColumn(
                name: "publisher_id",
                table: "workflow_templates");

            migrationBuilder.DropColumn(
                name: "changelog_json",
                table: "connector_definitions");

            migrationBuilder.DropColumn(
                name: "deprecated_at",
                table: "connector_definitions");

            migrationBuilder.DropColumn(
                name: "deprecation_note",
                table: "connector_definitions");

            migrationBuilder.DropColumn(
                name: "is_latest_major",
                table: "connector_definitions");

            migrationBuilder.DropColumn(
                name: "major_version",
                table: "connector_definitions");
        }
    }
}
