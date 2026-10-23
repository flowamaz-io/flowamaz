using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowEconomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "workflow_templates",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "usd");

            migrationBuilder.AddColumn<int>(
                name: "price_cents",
                table: "workflow_templates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "pricing_model",
                table: "workflow_templates",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "free");

            migrationBuilder.CreateTable(
                name: "publisher_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stripe_account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    charges_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    payouts_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    details_submitted = table.Column<bool>(type: "boolean", nullable: false),
                    country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    requirements_due = table.Column<string>(type: "jsonb", nullable: false),
                    status_refreshed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publisher_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "publisher_earnings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    publisher_org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    gross_cents = table.Column<int>(type: "integer", nullable: false),
                    fee_cents = table.Column<int>(type: "integer", nullable: false),
                    net_cents = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    stripe_object_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publisher_earnings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "template_purchases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    publisher_org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pricing_model = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount_cents = table.Column<int>(type: "integer", nullable: false),
                    fee_cents = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    stripe_checkout_session_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    stripe_payment_intent_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    stripe_subscription_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    purchased_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    current_period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancel_at_period_end = table.Column<bool>(type: "boolean", nullable: false),
                    purchased_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_template_purchases", x => x.id);
                });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000001"),
                columns: new[] { "currency", "pricing_model" },
                values: new object[] { "usd", "free" });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000002"),
                columns: new[] { "currency", "pricing_model" },
                values: new object[] { "usd", "free" });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000003"),
                columns: new[] { "currency", "pricing_model" },
                values: new object[] { "usd", "free" });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000004"),
                columns: new[] { "currency", "pricing_model" },
                values: new object[] { "usd", "free" });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000005"),
                columns: new[] { "currency", "pricing_model" },
                values: new object[] { "usd", "free" });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000006"),
                columns: new[] { "currency", "pricing_model" },
                values: new object[] { "usd", "free" });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000007"),
                columns: new[] { "currency", "pricing_model" },
                values: new object[] { "usd", "free" });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000008"),
                columns: new[] { "currency", "pricing_model" },
                values: new object[] { "usd", "free" });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000009"),
                columns: new[] { "currency", "pricing_model" },
                values: new object[] { "usd", "free" });

            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000010"),
                columns: new[] { "currency", "pricing_model" },
                values: new object[] { "usd", "free" });

            migrationBuilder.CreateIndex(
                name: "ix_publisher_accounts_org_id",
                table: "publisher_accounts",
                column: "org_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_publisher_accounts_stripe_account_id",
                table: "publisher_accounts",
                column: "stripe_account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_publisher_earnings_publisher_org_id_occurred_at",
                table: "publisher_earnings",
                columns: new[] { "publisher_org_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_publisher_earnings_stripe_object_id_type",
                table: "publisher_earnings",
                columns: new[] { "stripe_object_id", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_template_purchases_publisher_org_id",
                table: "template_purchases",
                column: "publisher_org_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_purchases_stripe_checkout_session_id",
                table: "template_purchases",
                column: "stripe_checkout_session_id",
                unique: true,
                filter: "stripe_checkout_session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_template_purchases_stripe_payment_intent_id",
                table: "template_purchases",
                column: "stripe_payment_intent_id",
                filter: "stripe_payment_intent_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_template_purchases_stripe_subscription_id",
                table: "template_purchases",
                column: "stripe_subscription_id",
                filter: "stripe_subscription_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_template_purchases_workspace_id_template_id",
                table: "template_purchases",
                columns: new[] { "workspace_id", "template_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "publisher_accounts");

            migrationBuilder.DropTable(
                name: "publisher_earnings");

            migrationBuilder.DropTable(
                name: "template_purchases");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "workflow_templates");

            migrationBuilder.DropColumn(
                name: "price_cents",
                table: "workflow_templates");

            migrationBuilder.DropColumn(
                name: "pricing_model",
                table: "workflow_templates");
        }
    }
}
