using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialScaffold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_token_usage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    function_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tokens_input = table.Column<int>(type: "integer", nullable: false),
                    tokens_output = table.Column<int>(type: "integer", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_token_usage", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "model_catalogue",
                columns: table => new
                {
                    model_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_model_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    has_vision = table.Column<bool>(type: "boolean", nullable: false),
                    max_context_tokens = table.Column<int>(type: "integer", nullable: false),
                    supports_json_mode = table.Column<bool>(type: "boolean", nullable: false),
                    supports_streaming = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    plan_access = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_model_catalogue", x => x.model_id);
                });

            migrationBuilder.CreateTable(
                name: "org_ai_configs",
                columns: table => new
                {
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    providers_enabled = table.Column<string>(type: "jsonb", nullable: false),
                    function_overrides_json = table.Column<string>(type: "jsonb", nullable: false),
                    workspace_locked_functions = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_ai_configs", x => x.org_id);
                });

            migrationBuilder.CreateTable(
                name: "platform_ai_configs",
                columns: table => new
                {
                    function_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    key_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    plan_access = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_ai_configs", x => x.function_id);
                });

            migrationBuilder.CreateTable(
                name: "workspace_ai_budgets",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monthly_token_limit = table.Column<long>(type: "bigint", nullable: false),
                    tokens_used_this_month = table.Column<long>(type: "bigint", nullable: false),
                    budget_reset_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_hard_capped = table.Column<bool>(type: "boolean", nullable: false),
                    alert_sent_at80pct = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_ai_budgets", x => x.workspace_id);
                });

            migrationBuilder.CreateTable(
                name: "workspace_ai_configs",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    function_overrides_json = table.Column<string>(type: "jsonb", nullable: false),
                    byok_credential_refs_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_ai_configs", x => x.workspace_id);
                });

            migrationBuilder.InsertData(
                table: "model_catalogue",
                columns: new[] { "model_id", "created_at", "display_name", "has_vision", "is_enabled", "max_context_tokens", "plan_access", "provider", "provider_model_id", "supports_json_mode", "supports_streaming", "updated_at" },
                values: new object[,]
                {
                    { "claude-haiku-4-5", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Claude Haiku 4.5", false, true, 200000, "[\"community\",\"starter\",\"pro\",\"enterprise\"]", "anthropic", "claude-haiku-4-5", true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "claude-opus-4-6", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Claude Opus 4.6", true, true, 200000, "[\"community\",\"starter\",\"pro\",\"enterprise\"]", "anthropic", "claude-opus-4-6", true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "claude-sonnet-4-6", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Claude Sonnet 4.6", true, true, 200000, "[\"community\",\"starter\",\"pro\",\"enterprise\"]", "anthropic", "claude-sonnet-4-6", true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "gemini-1-5-flash", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Gemini 1.5 Flash", true, true, 1000000, "[\"starter\",\"pro\",\"enterprise\"]", "google", "gemini-1.5-flash", true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "gemini-1-5-pro", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Gemini 1.5 Pro", true, true, 1000000, "[\"starter\",\"pro\",\"enterprise\"]", "google", "gemini-1.5-pro", true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "gemini-2-0-flash", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Gemini 2.0 Flash", true, true, 1000000, "[\"starter\",\"pro\",\"enterprise\"]", "google", "gemini-2.0-flash", true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "gpt-4o", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "GPT-4o", true, true, 128000, "[\"starter\",\"pro\",\"enterprise\"]", "azure-openai", "gpt-4o", true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "gpt-4o-mini", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "GPT-4o mini", false, true, 128000, "[\"starter\",\"pro\",\"enterprise\"]", "azure-openai", "gpt-4o-mini", true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "mistral-large", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Mistral Large", false, true, 128000, "[\"starter\",\"pro\",\"enterprise\"]", "mistral", "mistral-large", true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "mistral-medium", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Mistral Medium", false, true, 32000, "[\"starter\",\"pro\",\"enterprise\"]", "mistral", "mistral-medium", true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "mistral-nemo", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Mistral Nemo", false, true, 128000, "[\"starter\",\"pro\",\"enterprise\"]", "mistral", "mistral-nemo", true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "moonshot-v1-128k", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Moonshot v1 128k", false, true, 128000, "[\"starter\",\"pro\",\"enterprise\"]", "kimi", "moonshot-v1-128k", true, false, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "moonshot-v1-32k", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Moonshot v1 32k", false, true, 32000, "[\"starter\",\"pro\",\"enterprise\"]", "kimi", "moonshot-v1-32k", true, false, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "moonshot-v1-8k", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Moonshot v1 8k", false, true, 8000, "[\"starter\",\"pro\",\"enterprise\"]", "kimi", "moonshot-v1-8k", true, false, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "o1-mini", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "o1-mini", false, true, 128000, "[\"starter\",\"pro\",\"enterprise\"]", "azure-openai", "o1-mini", false, false, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                table: "platform_ai_configs",
                columns: new[] { "function_id", "created_at", "is_enabled", "key_source", "model_id", "plan_access", "provider", "updated_at" },
                values: new object[,]
                {
                    { "copilot", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Platform", "claude-haiku-4-5", "[\"community\",\"starter\",\"pro\",\"enterprise\"]", "anthropic", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "doc-parse", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Platform", "claude-sonnet-4-6", "[\"community\",\"starter\",\"pro\",\"enterprise\"]", "anthropic", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "help-assist", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Platform", "claude-sonnet-4-6", "[\"community\",\"starter\",\"pro\",\"enterprise\"]", "anthropic", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "nl-yaml", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Platform", "claude-sonnet-4-6", "[\"community\",\"starter\",\"pro\",\"enterprise\"]", "anthropic", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "node-exec", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Byok", "claude-haiku-4-5", "[\"community\",\"starter\",\"pro\",\"enterprise\"]", "anthropic", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "process-intel", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Platform", "claude-haiku-4-5", "[\"community\",\"starter\",\"pro\",\"enterprise\"]", "anthropic", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "visual-input", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Platform", "claude-sonnet-4-6", "[\"community\",\"starter\",\"pro\",\"enterprise\"]", "anthropic", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_token_usage_function_id_created_at",
                table: "ai_token_usage",
                columns: new[] { "function_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_token_usage_org_id_created_at",
                table: "ai_token_usage",
                columns: new[] { "org_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_token_usage_workspace_id_created_at",
                table: "ai_token_usage",
                columns: new[] { "workspace_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_model_catalogue_provider",
                table: "model_catalogue",
                column: "provider");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_ai_budgets_budget_reset_date",
                table: "workspace_ai_budgets",
                column: "budget_reset_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_token_usage");

            migrationBuilder.DropTable(
                name: "model_catalogue");

            migrationBuilder.DropTable(
                name: "org_ai_configs");

            migrationBuilder.DropTable(
                name: "platform_ai_configs");

            migrationBuilder.DropTable(
                name: "workspace_ai_budgets");

            migrationBuilder.DropTable(
                name: "workspace_ai_configs");
        }
    }
}
