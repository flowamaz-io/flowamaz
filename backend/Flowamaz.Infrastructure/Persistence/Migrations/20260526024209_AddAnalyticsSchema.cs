using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalyticsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_insights",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    insight_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    is_acknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    acknowledged_by = table.Column<Guid>(type: "uuid", nullable: true),
                    acknowledged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_workflow_insights", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_metrics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_hour = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    runs_total = table.Column<int>(type: "integer", nullable: false),
                    runs_completed = table.Column<int>(type: "integer", nullable: false),
                    runs_failed = table.Column<int>(type: "integer", nullable: false),
                    runs_cancelled = table.Column<int>(type: "integer", nullable: false),
                    avg_duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    p95duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    p99duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    sla_breach_count = table.Column<int>(type: "integer", nullable: false),
                    sla_threshold_ms = table.Column<long>(type: "bigint", nullable: true),
                    bottleneck_node_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    bottleneck_avg_ms = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("pk_workflow_metrics", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_insights_workflow_definition_id",
                table: "workflow_insights",
                column: "workflow_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_insights_workspace_id_is_acknowledged_created_at",
                table: "workflow_insights",
                columns: new[] { "workspace_id", "is_acknowledged", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_metrics_workflow_definition_id_period_hour",
                table: "workflow_metrics",
                columns: new[] { "workflow_definition_id", "period_hour" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_metrics_workspace_id",
                table: "workflow_metrics",
                column: "workspace_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_insights");

            migrationBuilder.DropTable(
                name: "workflow_metrics");
        }
    }
}
