using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformOrganisationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "org_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_org_owner = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lockout_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failed_login_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "organisations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    billing_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    stripe_customer_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    trial_ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_region = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organisations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    price_monthly_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    price_annual_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    limits = table.Column<string>(type: "jsonb", nullable: false),
                    features = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    billing_cycle = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    current_period_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    current_period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    overcap_cap_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usage_aggregates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    runs_count = table.Column<long>(type: "bigint", nullable: false),
                    ai_calls_count = table.Column<long>(type: "bigint", nullable: false),
                    ai_cost_usd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    storage_bytes = table.Column<long>(type: "bigint", nullable: false),
                    member_peak = table.Column<int>(type: "integer", nullable: false),
                    api_calls_count = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_aggregates", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "plans",
                columns: new[] { "id", "created_at", "features", "is_active", "is_public", "limits", "name", "price_annual_usd", "price_monthly_usd", "slug", "updated_at" },
                values: new object[,]
                {
                    { new Guid("a1000000-0000-0000-0000-000000000001"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "{\"sso\":false,\"scim\":false,\"customConnectors\":false,\"auditLog\":false,\"apiAccess\":false,\"byom\":false,\"dataResidency\":false,\"cmek\":false,\"processIntelligence\":false,\"roiAnalytics\":false}", true, true, "{\"maxWorkspaces\":1,\"maxMembersPerWorkspace\":1,\"maxWorkflowDefinitions\":5,\"maxRunsPerMonth\":500,\"maxAiCallsPerMonth\":0,\"maxStorageGb\":1,\"runRetentionDays\":7,\"apiRateLimitPerMinute\":10,\"allowedAiProviders\":[\"anthropic\"]}", "Community", 0m, 0m, "community", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("a1000000-0000-0000-0000-000000000002"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "{\"sso\":false,\"scim\":false,\"customConnectors\":true,\"auditLog\":false,\"apiAccess\":true,\"byom\":false,\"dataResidency\":false,\"cmek\":false,\"processIntelligence\":false,\"roiAnalytics\":false}", true, true, "{\"maxWorkspaces\":3,\"maxMembersPerWorkspace\":10,\"maxWorkflowDefinitions\":20,\"maxRunsPerMonth\":5000,\"maxAiCallsPerMonth\":0,\"maxStorageGb\":10,\"runRetentionDays\":90,\"apiRateLimitPerMinute\":60,\"allowedAiProviders\":[\"anthropic\",\"azure-openai\",\"google\"]}", "Starter", 490m, 49m, "starter", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("a1000000-0000-0000-0000-000000000003"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "{\"sso\":false,\"scim\":false,\"customConnectors\":true,\"auditLog\":true,\"apiAccess\":true,\"byom\":false,\"dataResidency\":false,\"cmek\":false,\"processIntelligence\":true,\"roiAnalytics\":true}", true, true, "{\"maxWorkspaces\":10,\"maxMembersPerWorkspace\":50,\"maxWorkflowDefinitions\":0,\"maxRunsPerMonth\":50000,\"maxAiCallsPerMonth\":0,\"maxStorageGb\":50,\"runRetentionDays\":365,\"apiRateLimitPerMinute\":300,\"allowedAiProviders\":[\"anthropic\",\"azure-openai\",\"google\",\"kimi\",\"mistral\"]}", "Pro", 1990m, 199m, "pro", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("a1000000-0000-0000-0000-000000000004"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "{\"sso\":true,\"scim\":true,\"customConnectors\":true,\"auditLog\":true,\"apiAccess\":true,\"byom\":true,\"dataResidency\":true,\"cmek\":true,\"processIntelligence\":true,\"roiAnalytics\":true}", true, false, "{\"maxWorkspaces\":0,\"maxMembersPerWorkspace\":0,\"maxWorkflowDefinitions\":0,\"maxRunsPerMonth\":0,\"maxAiCallsPerMonth\":0,\"maxStorageGb\":0,\"runRetentionDays\":0,\"apiRateLimitPerMinute\":0,\"allowedAiProviders\":[\"anthropic\",\"azure-openai\",\"google\",\"kimi\",\"mistral\",\"byom\"]}", "Enterprise", 0m, 0m, "enterprise", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_org_users_org_id_email",
                table: "org_users",
                columns: new[] { "org_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organisations_plan_id",
                table: "organisations",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_organisations_slug",
                table: "organisations",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plans_slug",
                table: "plans",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_org_id",
                table: "subscriptions",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "ix_usage_aggregates_org_id_period_month",
                table: "usage_aggregates",
                columns: new[] { "org_id", "period_month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "org_users");

            migrationBuilder.DropTable(
                name: "organisations");

            migrationBuilder.DropTable(
                name: "plans");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "usage_aggregates");
        }
    }
}
