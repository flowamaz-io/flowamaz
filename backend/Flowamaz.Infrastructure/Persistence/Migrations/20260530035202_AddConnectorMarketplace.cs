using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorMarketplace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "average_rating",
                table: "connector_definitions",
                type: "numeric(3,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "install_count",
                table: "connector_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_official",
                table: "connector_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_updated_at",
                table: "connector_definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rating_count",
                table: "connector_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "connector_ratings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    review = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connector_ratings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "connector_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    manifest_yaml = table.Column<string>(type: "text", nullable: false),
                    github_pr_url = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reviewer_notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connector_submissions", x => x.id);
                });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000001"),
                columns: new[] { "average_rating", "install_count", "is_official", "last_updated_at", "rating_count" },
                values: new object[] { 0m, 0, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000002"),
                columns: new[] { "average_rating", "install_count", "is_official", "last_updated_at", "rating_count" },
                values: new object[] { 0m, 0, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000003"),
                columns: new[] { "average_rating", "install_count", "is_official", "last_updated_at", "rating_count" },
                values: new object[] { 0m, 0, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000004"),
                columns: new[] { "average_rating", "install_count", "is_official", "last_updated_at", "rating_count" },
                values: new object[] { 0m, 0, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000005"),
                columns: new[] { "average_rating", "install_count", "is_official", "last_updated_at", "rating_count" },
                values: new object[] { 0m, 0, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000006"),
                columns: new[] { "average_rating", "install_count", "is_official", "last_updated_at", "rating_count" },
                values: new object[] { 0m, 0, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000007"),
                columns: new[] { "average_rating", "install_count", "is_official", "last_updated_at", "rating_count" },
                values: new object[] { 0m, 0, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000008"),
                columns: new[] { "average_rating", "install_count", "is_official", "last_updated_at", "rating_count" },
                values: new object[] { 0m, 0, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000009"),
                columns: new[] { "average_rating", "install_count", "is_official", "last_updated_at", "rating_count" },
                values: new object[] { 0m, 0, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000010"),
                columns: new[] { "average_rating", "install_count", "is_official", "last_updated_at", "rating_count" },
                values: new object[] { 0m, 0, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000011"),
                columns: new[] { "average_rating", "install_count", "is_official", "last_updated_at", "rating_count" },
                values: new object[] { 0m, 0, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000012"),
                columns: new[] { "average_rating", "install_count", "is_official", "last_updated_at", "rating_count" },
                values: new object[] { 0m, 0, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0 });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000013"),
                columns: new[] { "average_rating", "install_count", "is_official", "last_updated_at", "rating_count" },
                values: new object[] { 0m, 0, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0 });

            migrationBuilder.CreateIndex(
                name: "ix_connector_ratings_connector_definition_id_org_id",
                table: "connector_ratings",
                columns: new[] { "connector_definition_id", "org_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_connector_submissions_org_id",
                table: "connector_submissions",
                column: "org_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connector_ratings");

            migrationBuilder.DropTable(
                name: "connector_submissions");

            migrationBuilder.DropColumn(
                name: "average_rating",
                table: "connector_definitions");

            migrationBuilder.DropColumn(
                name: "install_count",
                table: "connector_definitions");

            migrationBuilder.DropColumn(
                name: "is_official",
                table: "connector_definitions");

            migrationBuilder.DropColumn(
                name: "last_updated_at",
                table: "connector_definitions");

            migrationBuilder.DropColumn(
                name: "rating_count",
                table: "connector_definitions");
        }
    }
}
