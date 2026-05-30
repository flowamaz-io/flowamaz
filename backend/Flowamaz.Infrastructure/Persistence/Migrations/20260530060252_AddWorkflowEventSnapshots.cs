using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowEventSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "parent_instance_id",
                table: "workflow_instances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "duration_ms",
                table: "workflow_events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "error_snapshot",
                table: "workflow_events",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "input_snapshot",
                table: "workflow_events",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "output_snapshot",
                table: "workflow_events",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_parent_instance_id",
                table: "workflow_instances",
                column: "parent_instance_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_workflow_instances_parent_instance_id",
                table: "workflow_instances");

            migrationBuilder.DropColumn(
                name: "parent_instance_id",
                table: "workflow_instances");

            migrationBuilder.DropColumn(
                name: "duration_ms",
                table: "workflow_events");

            migrationBuilder.DropColumn(
                name: "error_snapshot",
                table: "workflow_events");

            migrationBuilder.DropColumn(
                name: "input_snapshot",
                table: "workflow_events");

            migrationBuilder.DropColumn(
                name: "output_snapshot",
                table: "workflow_events");
        }
    }
}
