using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSsoAccessRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "default_role",
                table: "org_sso_configs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Viewer");

            migrationBuilder.AddColumn<string>(
                name: "group_role_mappings",
                table: "org_sso_configs",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "groups_attribute",
                table: "org_sso_configs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "groups");

            migrationBuilder.AddColumn<bool>(
                name: "jit_provisioning",
                table: "org_sso_configs",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "require_sso",
                table: "org_sso_configs",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_role",
                table: "org_sso_configs");

            migrationBuilder.DropColumn(
                name: "group_role_mappings",
                table: "org_sso_configs");

            migrationBuilder.DropColumn(
                name: "groups_attribute",
                table: "org_sso_configs");

            migrationBuilder.DropColumn(
                name: "jit_provisioning",
                table: "org_sso_configs");

            migrationBuilder.DropColumn(
                name: "require_sso",
                table: "org_sso_configs");
        }
    }
}
