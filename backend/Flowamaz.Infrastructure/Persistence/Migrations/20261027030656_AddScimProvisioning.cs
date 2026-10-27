using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScimProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "scim_deleted_at",
                table: "org_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scim_external_id",
                table: "org_users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scim_user_name",
                table: "org_users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "scim_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    external_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scim_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scim_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    token_prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scim_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scim_group_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scim_group_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_scim_group_members_scim_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "scim_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scim_group_members_group_id_org_user_id",
                table: "scim_group_members",
                columns: new[] { "group_id", "org_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scim_group_members_org_user_id",
                table: "scim_group_members",
                column: "org_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_scim_groups_org_id_display_name",
                table: "scim_groups",
                columns: new[] { "org_id", "display_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scim_tokens_org_id",
                table: "scim_tokens",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "ix_scim_tokens_token_hash",
                table: "scim_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scim_group_members");

            migrationBuilder.DropTable(
                name: "scim_tokens");

            migrationBuilder.DropTable(
                name: "scim_groups");

            migrationBuilder.DropColumn(
                name: "scim_deleted_at",
                table: "org_users");

            migrationBuilder.DropColumn(
                name: "scim_external_id",
                table: "org_users");

            migrationBuilder.DropColumn(
                name: "scim_user_name",
                table: "org_users");
        }
    }
}
