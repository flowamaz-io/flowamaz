using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Production query-pattern indexes from the index audit (prompt 07-06). Raw SQL only — these
    /// are partial / DESC indexes that EF's model would not represent identically, so they are kept
    /// out of the model snapshot (no HasIndex) to avoid snapshot drift. CREATE/DROP use IF [NOT]
    /// EXISTS so the migration is idempotent and safe alongside the functionally-similar
    /// EF-managed indexes that already exist (e.g. ix_ai_token_usage_workspace_id_created_at).
    /// </summary>
    public partial class AddProductionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Instance queries (most frequent) — partial on is_deleted = false.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_workflow_instances_workspace_status " +
                "ON workflow_instances (workspace_id, status) WHERE is_deleted = false;");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_workflow_instances_workspace_created " +
                "ON workflow_instances (workspace_id, created_at DESC) WHERE is_deleted = false;");

            // Audit log queries — newest-first listing.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_audit_events_workspace_created " +
                "ON audit_events (workspace_id, created_at DESC);");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_audit_events_org_created " +
                "ON audit_events (org_id, created_at DESC);");

            // Workflow event timeline. Column is instance_id (not workflow_instance_id).
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_workflow_events_instance_created " +
                "ON workflow_events (instance_id, created_at ASC);");

            // Notification feed — unread-first, newest-first.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_notifications_user_read " +
                "ON notifications (user_id, is_read, created_at DESC);");

            // AI token usage analytics — newest-first per workspace.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_ai_token_usage_workspace_created " +
                "ON ai_token_usage (workspace_id, created_at DESC);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_workflow_instances_workspace_status;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_workflow_instances_workspace_created;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_audit_events_workspace_created;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_audit_events_org_created;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_workflow_events_instance_created;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_notifications_user_read;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_ai_token_usage_workspace_created;");
        }
    }
}
