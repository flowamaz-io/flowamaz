using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScimRoleResyncQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scim_role_resyncs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scim_role_resyncs", x => x.id);
                    table.ForeignKey(
                        name: "fk_scim_role_resyncs_org_users_org_user_id",
                        column: x => x.org_user_id,
                        principalTable: "org_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scim_role_resyncs_created_at",
                table: "scim_role_resyncs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_scim_role_resyncs_org_id_org_user_id",
                table: "scim_role_resyncs",
                columns: new[] { "org_id", "org_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scim_role_resyncs_org_user_id",
                table: "scim_role_resyncs",
                column: "org_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scim_role_resyncs");
        }
    }
}
