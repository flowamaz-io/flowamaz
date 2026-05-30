using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeBillingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "canceled_at",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "payment_failed",
                table: "subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "stripe_subscription_id",
                table: "subscriptions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "trial_ends_at",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stripe_price_id_annual",
                table: "plans",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stripe_price_id_monthly",
                table: "plans",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "plans",
                keyColumn: "id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000001"),
                columns: new[] { "stripe_price_id_annual", "stripe_price_id_monthly" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "plans",
                keyColumn: "id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000002"),
                columns: new[] { "stripe_price_id_annual", "stripe_price_id_monthly" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "plans",
                keyColumn: "id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000003"),
                columns: new[] { "stripe_price_id_annual", "stripe_price_id_monthly" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "plans",
                keyColumn: "id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000004"),
                columns: new[] { "stripe_price_id_annual", "stripe_price_id_monthly" },
                values: new object[] { null, null });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_stripe_subscription_id",
                table: "subscriptions",
                column: "stripe_subscription_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_subscriptions_stripe_subscription_id",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "canceled_at",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "payment_failed",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "stripe_subscription_id",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "trial_ends_at",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "stripe_price_id_annual",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "stripe_price_id_monthly",
                table: "plans");
        }
    }
}
