using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gate_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_to_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    delivery_channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    delivery_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    delivery_error = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    escalated_to = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_gate_decisions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    yaml_content = table.Column<string>(type: "text", nullable: false),
                    nl_description = table.Column<string>(type: "text", nullable: true),
                    created_by_method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    current_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    health_score = table.Column<int>(type: "integer", nullable: false),
                    trigger_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    table.PrimaryKey("pk_workflow_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    node_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    node_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    trigger_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    trigger_payload = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    current_node_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    worker_lease_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    worker_lease_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    saga_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    saga_strategy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
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
                    table.PrimaryKey("pk_workflow_instances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_node_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    node_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    input_payload = table.Column<string>(type: "jsonb", nullable: true),
                    output_payload = table.Column<string>(type: "jsonb", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    last_retry_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_workflow_node_states", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_variables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_workflow_variables", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    commit_sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tag_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    branch_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    yaml_content = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    is_production = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_workflow_versions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gate_decisions_assigned_to",
                table: "gate_decisions",
                column: "assigned_to");

            migrationBuilder.CreateIndex(
                name: "ix_gate_decisions_expires_at",
                table: "gate_decisions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_gate_decisions_instance_id_node_id",
                table: "gate_decisions",
                columns: new[] { "instance_id", "node_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gate_decisions_workspace_id",
                table: "gate_decisions",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_workspace_id",
                table: "workflow_definitions",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_workspace_id_slug",
                table: "workflow_definitions",
                columns: new[] { "workspace_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_events_instance_id_occurred_at",
                table: "workflow_events",
                columns: new[] { "instance_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_events_instance_id_sequence_number",
                table: "workflow_events",
                columns: new[] { "instance_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_events_workspace_id",
                table: "workflow_events",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_idempotency_key",
                table: "workflow_instances",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_status_worker_lease_expires_at",
                table: "workflow_instances",
                columns: new[] { "status", "worker_lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_workspace_id",
                table: "workflow_instances",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_states_instance_id_node_id",
                table: "workflow_node_states",
                columns: new[] { "instance_id", "node_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_states_workspace_id",
                table: "workflow_node_states",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_variables_instance_id_name",
                table: "workflow_variables",
                columns: new[] { "instance_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_variables_workspace_id",
                table: "workflow_variables",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_versions_workflow_definition_id_commit_sha",
                table: "workflow_versions",
                columns: new[] { "workflow_definition_id", "commit_sha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_versions_workspace_id",
                table: "workflow_versions",
                column: "workspace_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gate_decisions");

            migrationBuilder.DropTable(
                name: "workflow_definitions");

            migrationBuilder.DropTable(
                name: "workflow_events");

            migrationBuilder.DropTable(
                name: "workflow_instances");

            migrationBuilder.DropTable(
                name: "workflow_node_states");

            migrationBuilder.DropTable(
                name: "workflow_variables");

            migrationBuilder.DropTable(
                name: "workflow_versions");
        }
    }
}
