using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenSsoSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "managed_by_sso",
                table: "workspace_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "sso_subject",
                table: "org_users",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "allow_idp_initiated",
                table: "org_sso_configs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "allowed_email_domains",
                table: "org_sso_configs",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "default_workspace_ids",
                table: "org_sso_configs",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "managed_by_sso",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "sso_subject",
                table: "org_users");

            migrationBuilder.DropColumn(
                name: "allow_idp_initiated",
                table: "org_sso_configs");

            migrationBuilder.DropColumn(
                name: "allowed_email_domains",
                table: "org_sso_configs");

            migrationBuilder.DropColumn(
                name: "default_workspace_ids",
                table: "org_sso_configs");
        }
    }
}
