using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGateTimeoutActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "escalate_to_email",
                table: "gate_decisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "escalation_count",
                table: "gate_decisions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "on_timeout",
                table: "gate_decisions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "fail");

            migrationBuilder.AddColumn<int>(
                name: "timeout_hours",
                table: "gate_decisions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "escalate_to_email",
                table: "gate_decisions");

            migrationBuilder.DropColumn(
                name: "escalation_count",
                table: "gate_decisions");

            migrationBuilder.DropColumn(
                name: "on_timeout",
                table: "gate_decisions");

            migrationBuilder.DropColumn(
                name: "timeout_hours",
                table: "gate_decisions");
        }
    }
}
