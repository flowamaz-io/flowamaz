using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIsTestToWorkflowInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_test",
                table: "workflow_instances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "test_expires_at",
                table: "workflow_instances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_is_test_test_expires_at",
                table: "workflow_instances",
                columns: new[] { "is_test", "test_expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_workflow_instances_is_test_test_expires_at",
                table: "workflow_instances");

            migrationBuilder.DropColumn(
                name: "is_test",
                table: "workflow_instances");

            migrationBuilder.DropColumn(
                name: "test_expires_at",
                table: "workflow_instances");
        }
    }
}
