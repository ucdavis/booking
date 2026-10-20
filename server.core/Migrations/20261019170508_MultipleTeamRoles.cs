using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Core.Migrations
{
    /// <inheritdoc />
    public partial class MultipleTeamRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_TeamPermissions",
                table: "TeamPermissions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TeamPermissions",
                table: "TeamPermissions",
                columns: new[] { "TeamId", "UserId", "Role" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_TeamPermissions",
                table: "TeamPermissions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TeamPermissions",
                table: "TeamPermissions",
                columns: new[] { "TeamId", "UserId" });
        }
    }
}
