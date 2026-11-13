using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryPublishers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "review_notes",
                table: "workflow_templates",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at",
                table: "workflow_templates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "reviewer_notes",
                table: "connector_submissions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "connector_id",
                table: "connector_submissions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "decided_at",
                table: "connector_submissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "pr_checked_at",
                table: "connector_submissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "publisher_id",
                table: "connector_submissions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "publisher_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    publisher_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    bio = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    website_url = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    github_login = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: true),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publisher_profiles", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "publisher_profiles",
                columns: new[] { "id", "bio", "created_at", "created_by", "deleted_at", "display_name", "github_login", "is_deleted", "is_verified", "org_id", "publisher_id", "updated_at", "updated_by", "website_url" },
                values: new object[] { new Guid("00000000-0000-0000-0007-000000000001"), "Official connectors, templates, forms and packs, built and maintained by the Flowamaz team.", new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Flowamaz", "flowamaz-io", false, true, null, "flowamaz-io", new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Utc), null, "https://flowamaz.com" });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000001"),
                columns: new[] { "review_notes", "reviewed_at" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000002"),
                columns: new[] { "review_notes", "reviewed_at" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000003"),
                columns: new[] { "review_notes", "reviewed_at" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000004"),
                columns: new[] { "review_notes", "reviewed_at" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000005"),
                columns: new[] { "review_notes", "reviewed_at" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000006"),
                columns: new[] { "review_notes", "reviewed_at" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000007"),
                columns: new[] { "review_notes", "reviewed_at" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000008"),
                columns: new[] { "review_notes", "reviewed_at" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000009"),
                columns: new[] { "review_notes", "reviewed_at" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000010"),
                columns: new[] { "review_notes", "reviewed_at" },
                values: new object[] { null, null });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_templates_review_status",
                table: "workflow_templates",
                column: "review_status");

            migrationBuilder.CreateIndex(
                name: "ix_connector_submissions_status",
                table: "connector_submissions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_publisher_profiles_org_id",
                table: "publisher_profiles",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "ix_publisher_profiles_publisher_id",
                table: "publisher_profiles",
                column: "publisher_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "publisher_profiles");

            migrationBuilder.DropIndex(
                name: "ix_workflow_templates_review_status",
                table: "workflow_templates");

            migrationBuilder.DropIndex(
                name: "ix_connector_submissions_status",
                table: "connector_submissions");

            migrationBuilder.DropColumn(
                name: "review_notes",
                table: "workflow_templates");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "workflow_templates");

            migrationBuilder.DropColumn(
                name: "connector_id",
                table: "connector_submissions");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "connector_submissions");

            migrationBuilder.DropColumn(
                name: "pr_checked_at",
                table: "connector_submissions");

            migrationBuilder.DropColumn(
                name: "publisher_id",
                table: "connector_submissions");

            migrationBuilder.AlterColumn<string>(
                name: "reviewer_notes",
                table: "connector_submissions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);
        }
    }
}
