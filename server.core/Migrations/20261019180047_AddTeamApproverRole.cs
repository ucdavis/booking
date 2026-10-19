using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamApproverRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TeamPermissions_Role",
                table: "TeamPermissions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TeamPermissions_Role",
                table: "TeamPermissions",
                sql: "[Role] IN ('admin', 'editor', 'viewer', 'approver')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TeamPermissions_Role",
                table: "TeamPermissions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TeamPermissions_Role",
                table: "TeamPermissions",
                sql: "[Role] IN ('admin', 'editor', 'viewer')");
        }
    }
}
