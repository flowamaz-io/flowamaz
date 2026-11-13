using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryFormsAndPacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "form_json",
                table: "gate_decisions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "form_values_json",
                table: "gate_decisions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "form_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    publisher_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    is_official = table.Column<bool>(type: "boolean", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fields_json = table.Column<string>(type: "jsonb", nullable: false),
                    tags = table.Column<string>(type: "jsonb", nullable: false),
                    deprecated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deprecation_note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pack_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    publisher_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    is_official = table.Column<bool>(type: "boolean", nullable: false),
                    connectors = table.Column<string>(type: "jsonb", nullable: false),
                    templates = table.Column<string>(type: "jsonb", nullable: false),
                    forms = table.Column<string>(type: "jsonb", nullable: false),
                    tags = table.Column<string>(type: "jsonb", nullable: false),
                    deprecated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deprecation_note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pack_definitions", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "form_definitions",
                columns: new[] { "id", "category", "created_at", "created_by", "deleted_at", "deprecated_at", "deprecation_note", "description", "fields_json", "is_deleted", "is_official", "name", "publisher_id", "slug", "tags", "updated_at", "updated_by", "version", "workspace_id" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0005-000000000001"), "Finance", new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "What the approver confirms on a purchase request: the amount, cost centre and budget line.", "[{\"key\":\"approved_amount\",\"label\":\"Approved amount\",\"type\":\"number\",\"required\":true,\"options\":null,\"help\":\"The amount you approve; it can be lower than requested.\",\"min\":0,\"max\":null},{\"key\":\"cost_centre\",\"label\":\"Cost centre\",\"type\":\"text\",\"required\":true,\"options\":null,\"help\":null,\"min\":null,\"max\":null},{\"key\":\"budget_line\",\"label\":\"Budget line\",\"type\":\"select\",\"required\":false,\"options\":[\"Operating\",\"Capital\",\"Project\"],\"help\":null,\"min\":null,\"max\":null},{\"key\":\"conditions\",\"label\":\"Conditions\",\"type\":\"textarea\",\"required\":false,\"options\":null,\"help\":\"Anything the requester must do, e.g. get two quotes.\",\"min\":null,\"max\":null}]", false, true, "Purchase details", "flowamaz-io", "purchase-details", "[]", new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0", null },
                    { new Guid("00000000-0000-0000-0005-000000000002"), "Finance", new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "Checks before paying an expense claim: receipts, category and the amount to reimburse.", "[{\"key\":\"receipts_checked\",\"label\":\"I checked the receipts\",\"type\":\"checkbox\",\"required\":true,\"options\":null,\"help\":null,\"min\":null,\"max\":null},{\"key\":\"category\",\"label\":\"Category\",\"type\":\"select\",\"required\":true,\"options\":[\"Travel\",\"Meals\",\"Equipment\",\"Training\",\"Other\"],\"help\":null,\"min\":null,\"max\":null},{\"key\":\"reimburse_amount\",\"label\":\"Amount to reimburse\",\"type\":\"number\",\"required\":true,\"options\":null,\"help\":null,\"min\":0,\"max\":null}]", false, true, "Expense check", "flowamaz-io", "expense-check", "[]", new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0", null },
                    { new Guid("00000000-0000-0000-0005-000000000003"), "HR", new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "What a manager records when approving leave: the dates and who covers.", "[{\"key\":\"first_day\",\"label\":\"First day of leave\",\"type\":\"date\",\"required\":true,\"options\":null,\"help\":null,\"min\":null,\"max\":null},{\"key\":\"last_day\",\"label\":\"Last day of leave\",\"type\":\"date\",\"required\":true,\"options\":null,\"help\":null,\"min\":null,\"max\":null},{\"key\":\"cover\",\"label\":\"Who covers (email)\",\"type\":\"email\",\"required\":false,\"options\":null,\"help\":null,\"min\":null,\"max\":null}]", false, true, "Leave approval", "flowamaz-io", "leave-approval", "[]", new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0", null }
                });

            migrationBuilder.InsertData(
                table: "pack_definitions",
                columns: new[] { "id", "category", "connectors", "created_at", "created_by", "deleted_at", "deprecated_at", "deprecation_note", "description", "forms", "is_deleted", "is_official", "name", "publisher_id", "slug", "tags", "templates", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0006-000000000001"), "Finance", "[\"slack\",\"email-smtp\"]", new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "Purchase approval, expense claims and refunds, with Slack and email notices and the purchase and expense forms.", "[\"purchase-details\",\"expense-check\"]", false, true, "Finance approvals", "flowamaz-io", "finance-approvals", "[]", "[\"purchase-approval\",\"expense-claim\",\"customer-refund\"]", new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0" },
                    { new Guid("00000000-0000-0000-0006-000000000002"), "HR", "[\"microsoft-365\",\"email-smtp\"]", new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "Onboarding and leave requests with Microsoft 365 and email, and the leave approval form.", "[\"leave-approval\"]", false, true, "People operations", "flowamaz-io", "people-operations", "[]", "[\"employee-onboarding\",\"leave-request\"]", new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Utc), null, "1.0.0" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_form_definitions_workspace_id_slug",
                table: "form_definitions",
                columns: new[] { "workspace_id", "slug" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_pack_definitions_slug",
                table: "pack_definitions",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_definitions");

            migrationBuilder.DropTable(
                name: "pack_definitions");

            migrationBuilder.DropColumn(
                name: "form_json",
                table: "gate_decisions");

            migrationBuilder.DropColumn(
                name: "form_values_json",
                table: "gate_decisions");
        }
    }
}
