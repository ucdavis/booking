using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddUserKerberosAndRemovePeople : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.AddColumn<string>(
                name: "Kerberos",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kerberos",
                table: "Users");

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    IamId = table.Column<string>(type: "char(10)", maxLength: 10, nullable: false),
                    Email = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    EmployeeId = table.Column<string>(type: "char(8)", maxLength: 8, nullable: true),
                    ExternalId = table.Column<string>(type: "char(10)", maxLength: 10, nullable: true),
                    FirstIngestedAt = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IsActiveInIam = table.Column<bool>(type: "bit", nullable: false),
                    IsCampusEmployee = table.Column<string>(type: "char(1)", maxLength: 1, nullable: true),
                    IsEmployee = table.Column<bool>(type: "bit", nullable: true),
                    IsExternal = table.Column<bool>(type: "bit", nullable: true),
                    IsFaculty = table.Column<bool>(type: "bit", nullable: true),
                    IsHsEmployee = table.Column<bool>(type: "bit", nullable: true),
                    IsStaff = table.Column<bool>(type: "bit", nullable: true),
                    IsStudent = table.Column<bool>(type: "bit", nullable: true),
                    LastFetchedAt = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastRunId = table.Column<string>(type: "char(36)", maxLength: 36, nullable: true),
                    MiddleName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    ModifyDateRaw = table.Column<string>(type: "char(19)", maxLength: 19, nullable: true),
                    PrivacyCode = table.Column<string>(type: "char(1)", maxLength: 1, nullable: true),
                    PromotedAt = table.Column<DateTime>(type: "datetime2(6)", nullable: true),
                    PromotionRunId = table.Column<string>(type: "char(36)", maxLength: 36, nullable: true),
                    Pronouns = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SourceEndpoint = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    StudentId = table.Column<string>(type: "char(9)", maxLength: 9, nullable: true),
                    Suffix = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    UserId = table.Column<string>(type: "char(8)", maxLength: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.IamId)
                        .Annotation("SqlServer:Clustered", true);
                });
        }
    }
}
