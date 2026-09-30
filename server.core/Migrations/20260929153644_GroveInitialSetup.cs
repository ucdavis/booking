using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Core.Migrations
{
    /// <inheritdoc />
    public partial class GroveInitialSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Spaces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Slug = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsOfficialFacility = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Spaces", x => x.Id);
                    table.CheckConstraint("CK_Spaces_OfficialReference", "[IsOfficialFacility] = 0 OR [ReferenceNumber] IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PaymentsTeamSlug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PaymentsApiKeySecretName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IamId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    IsAdmin = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamSpaces",
                columns: table => new
                {
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    SpaceId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamSpaces", x => new { x.TeamId, x.SpaceId });
                    table.ForeignKey(
                        name: "FK_TeamSpaces_Spaces_SpaceId",
                        column: x => x.SpaceId,
                        principalTable: "Spaces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TeamSpaces_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Resources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SpaceId = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    ParentResourceId = table.Column<int>(type: "int", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Capacity = table.Column<int>(type: "int", nullable: true),
                    IsReservable = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ApprovalMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "manual"),
                    FollowTeamCalendar = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    PublicAccess = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AllowReservationSharing = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Resources", x => x.Id);
                    table.UniqueConstraint("AK_Resources_Id_SpaceId_TeamId", x => new { x.Id, x.SpaceId, x.TeamId });
                    table.CheckConstraint("CK_Resources_Approval", "ApprovalMode IN ('automatic', 'manual')");
                    table.CheckConstraint("CK_Resources_Capacity", "Capacity IS NULL OR Capacity > 0");
                    table.CheckConstraint("CK_Resources_PublicScope", "(ParentResourceId IS NULL AND PublicAccess IS NOT NULL AND PublicAccess IN ('none', 'availability', 'details')) OR (ParentResourceId IS NOT NULL AND PublicAccess IS NULL)");
                    table.ForeignKey(
                        name: "FK_Resources_Resources_ParentResourceId_SpaceId_TeamId",
                        columns: x => new { x.ParentResourceId, x.SpaceId, x.TeamId },
                        principalTable: "Resources",
                        principalColumns: new[] { "Id", "SpaceId", "TeamId" });
                    table.ForeignKey(
                        name: "FK_Resources_Spaces_SpaceId",
                        column: x => x.SpaceId,
                        principalTable: "Spaces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Resources_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Resources_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ResourceTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FormSchemaVersion = table.Column<int>(type: "int", nullable: false),
                    FormJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResourceDefaultsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceTemplates", x => x.Id);
                    table.CheckConstraint("CK_ResourceTemplates_Defaults", "ResourceDefaultsJson IS NULL OR ISJSON(ResourceDefaultsJson) = 1");
                    table.CheckConstraint("CK_ResourceTemplates_Form", "FormSchemaVersion > 0 AND ISJSON(FormJson) = 1");
                    table.ForeignKey(
                        name: "FK_ResourceTemplates_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ResourceTemplates_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TeamPermissions",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamPermissions", x => new { x.TeamId, x.UserId });
                    table.CheckConstraint("CK_TeamPermissions_Role", "[Role] IN ('admin', 'editor', 'viewer')");
                    table.ForeignKey(
                        name: "FK_TeamPermissions_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TeamPermissions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CalendarFeeds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: true),
                    SpaceId = table.Column<int>(type: "int", nullable: true),
                    ResourceId = table.Column<int>(type: "int", nullable: true),
                    DisplayMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "availability"),
                    ShareToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisabledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarFeeds", x => x.Id);
                    table.CheckConstraint("CK_CalendarFeeds_Display", "[DisplayMode] IN ('availability', 'titles', 'details')");
                    table.CheckConstraint("CK_CalendarFeeds_ResourceTeam", "[ResourceId] IS NULL OR [TeamId] IS NOT NULL");
                    table.CheckConstraint("CK_CalendarFeeds_Scope", "([SpaceId] IS NOT NULL AND [ResourceId] IS NULL) OR ([SpaceId] IS NULL AND [ResourceId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CalendarFeeds_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CalendarFeeds_Spaces_SpaceId",
                        column: x => x.SpaceId,
                        principalTable: "Spaces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CalendarFeeds_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CalendarFeeds_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CalendarFeeds_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Files",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SpaceId = table.Column<int>(type: "int", nullable: true),
                    ResourceId = table.Column<int>(type: "int", nullable: true),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Files", x => x.Id);
                    table.CheckConstraint("CK_Files_OneOwner", "(SpaceId IS NOT NULL AND ResourceId IS NULL) OR (SpaceId IS NULL AND ResourceId IS NOT NULL)");
                    table.CheckConstraint("CK_Files_Size", "SizeBytes >= 0");
                    table.ForeignKey(
                        name: "FK_Files_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Files_Spaces_SpaceId",
                        column: x => x.SpaceId,
                        principalTable: "Spaces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Files_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ScheduleExceptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamId = table.Column<int>(type: "int", nullable: true),
                    ResourceId = table.Column<int>(type: "int", nullable: true),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IntervalsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "manual"),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleExceptions", x => x.Id);
                    table.CheckConstraint("CK_ScheduleExceptions_Intervals", "(Kind = 'closed' AND IntervalsJson IS NULL) OR (Kind = 'hours' AND ResourceId IS NOT NULL AND IntervalsJson IS NOT NULL AND ISJSON(IntervalsJson) = 1)");
                    table.CheckConstraint("CK_ScheduleExceptions_Kind", "Kind IN ('closed', 'hours') AND Source IN ('manual', 'holiday_import')");
                    table.CheckConstraint("CK_ScheduleExceptions_Scope", "(TeamId IS NOT NULL AND ResourceId IS NULL) OR (TeamId IS NULL AND ResourceId IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ScheduleExceptions_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ScheduleExceptions_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ScheduleExceptions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ResourceConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    SourceTemplateId = table.Column<int>(type: "int", nullable: true),
                    FormSchemaVersion = table.Column<int>(type: "int", nullable: false),
                    FormJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DetailsSchemaVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedByUserId = table.Column<int>(type: "int", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceConfigs", x => x.Id);
                    table.UniqueConstraint("AK_ResourceConfigs_Id_ResourceId", x => new { x.Id, x.ResourceId });
                    table.CheckConstraint("CK_ResourceConfigs_Details", "DetailsSchemaVersion > 0 AND ISJSON(DetailsJson) = 1");
                    table.CheckConstraint("CK_ResourceConfigs_Form", "Version > 0 AND FormSchemaVersion > 0 AND ISJSON(FormJson) = 1");
                    table.CheckConstraint("CK_ResourceConfigs_Publication", "(PublishedAt IS NULL AND PublishedByUserId IS NULL) OR (PublishedAt IS NOT NULL AND PublishedByUserId IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ResourceConfigs_ResourceTemplates_SourceTemplateId",
                        column: x => x.SourceTemplateId,
                        principalTable: "ResourceTemplates",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ResourceConfigs_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ResourceConfigs_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ResourceConfigs_Users_PublishedByUserId",
                        column: x => x.PublishedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReservationSeries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    RequesterUserId = table.Column<int>(type: "int", nullable: false),
                    ResourceConfigId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FormResponsesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecurrenceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShareToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmissionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationSeries", x => x.Id);
                    table.CheckConstraint("CK_ReservationSeries_Recurrence", "[RecurrenceJson] IS NULL OR ISJSON([RecurrenceJson]) = 1");
                    table.CheckConstraint("CK_ReservationSeries_Responses", "ISJSON([FormResponsesJson]) = 1");
                    table.ForeignKey(
                        name: "FK_ReservationSeries_ResourceConfigs_ResourceConfigId_ResourceId",
                        columns: x => new { x.ResourceConfigId, x.ResourceId },
                        principalTable: "ResourceConfigs",
                        principalColumns: new[] { "Id", "ResourceId" });
                    table.ForeignKey(
                        name: "FK_ReservationSeries_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReservationSeries_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReservationSeries_Users_RequesterUserId",
                        column: x => x.RequesterUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReservationSeriesId = table.Column<int>(type: "int", nullable: false),
                    OccurrenceNumber = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ShareToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BillingDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BillingStatus = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false, defaultValue: "waiting"),
                    BillingPreparedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    BillingSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BillingDispatchStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaymentsInvoiceId = table.Column<int>(type: "int", nullable: true),
                    PaymentsLinkId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PaymentsStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PaymentsSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BillingAttentionRequired = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    BillingAttentionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.UniqueConstraint("AK_Reservations_Id_ReservationSeriesId", x => new { x.Id, x.ReservationSeriesId });
                    table.CheckConstraint("CK_Reservations_Attention", "[BillingStatus] <> 'needs_attention' OR ([BillingAttentionRequired] = 1 AND [BillingAttentionNote] IS NOT NULL)");
                    table.CheckConstraint("CK_Reservations_BillingStatus", "[BillingStatus] IN ('waiting', 'prepared', 'submitting', 'linked', 'free', 'skipped', 'needs_attention')");
                    table.CheckConstraint("CK_Reservations_Calculation", "([BillingPreparedAt] IS NULL AND [Amount] IS NULL AND [BillingSnapshotJson] IS NULL) OR ([BillingPreparedAt] IS NOT NULL AND [Amount] IS NOT NULL AND [Amount] >= 0 AND [BillingSnapshotJson] IS NOT NULL AND ISJSON([BillingSnapshotJson]) = 1)");
                    table.CheckConstraint("CK_Reservations_Dispatched", "[BillingDispatchStartedAt] IS NULL OR ([BillingPreparedAt] IS NOT NULL AND [Amount] > 0)");
                    table.CheckConstraint("CK_Reservations_ExternalInvoice", "[PaymentsInvoiceId] IS NULL OR [BillingDispatchStartedAt] IS NOT NULL");
                    table.CheckConstraint("CK_Reservations_Free", "[BillingStatus] <> 'free' OR ([BillingPreparedAt] IS NOT NULL AND [Amount] = 0 AND [BillingDispatchStartedAt] IS NULL AND [PaymentsInvoiceId] IS NULL)");
                    table.CheckConstraint("CK_Reservations_Interval", "[OccurrenceNumber] > 0 AND [Revision] > 0 AND [EndsAt] > [StartsAt]");
                    table.CheckConstraint("CK_Reservations_Linked", "[BillingStatus] <> 'linked' OR ([PaymentsInvoiceId] IS NOT NULL AND [PaymentsLinkId] IS NOT NULL)");
                    table.CheckConstraint("CK_Reservations_Prepared", "[BillingStatus] <> 'prepared' OR ([BillingPreparedAt] IS NOT NULL AND [Amount] > 0 AND [BillingDispatchStartedAt] IS NULL AND [PaymentsInvoiceId] IS NULL)");
                    table.CheckConstraint("CK_Reservations_Status", "[Status] IN ('pending', 'approved', 'rejected', 'canceled')");
                    table.CheckConstraint("CK_Reservations_Submitting", "[BillingStatus] NOT IN ('submitting', 'linked') OR [BillingDispatchStartedAt] IS NOT NULL");
                    table.CheckConstraint("CK_Reservations_Unprepared", "[BillingStatus] NOT IN ('waiting', 'skipped') OR ([BillingPreparedAt] IS NULL AND [BillingDispatchStartedAt] IS NULL AND [PaymentsInvoiceId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_Reservations_ReservationSeries_ReservationSeriesId",
                        column: x => x.ReservationSeriesId,
                        principalTable: "ReservationSeries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Reservations_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReservationSeriesId = table.Column<int>(type: "int", nullable: false),
                    ReservationId = table.Column<int>(type: "int", nullable: true),
                    RecipientUserId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DeduplicationKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.CheckConstraint("CK_Notifications_Payload", "[AttemptCount] >= 0 AND ISJSON([PayloadJson]) = 1");
                    table.ForeignKey(
                        name: "FK_Notifications_ReservationSeries_ReservationSeriesId",
                        column: x => x.ReservationSeriesId,
                        principalTable: "ReservationSeries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Notifications_Reservations_ReservationId_ReservationSeriesId",
                        columns: x => new { x.ReservationId, x.ReservationSeriesId },
                        principalTable: "Reservations",
                        principalColumns: new[] { "Id", "ReservationSeriesId" });
                    table.ForeignKey(
                        name: "FK_Notifications_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReservationEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReservationId = table.Column<int>(type: "int", nullable: false),
                    ReservationRevision = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationEvents", x => x.Id);
                    table.CheckConstraint("CK_ReservationEvents_Details", "[ReservationRevision] > 0 AND ISJSON([DetailsJson]) = 1");
                    table.ForeignKey(
                        name: "FK_ReservationEvents_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReservationEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarFeeds_CreatedByUserId",
                table: "CalendarFeeds",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarFeeds_ResourceId",
                table: "CalendarFeeds",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarFeeds_ShareToken",
                table: "CalendarFeeds",
                column: "ShareToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarFeeds_SpaceId",
                table: "CalendarFeeds",
                column: "SpaceId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarFeeds_TeamId",
                table: "CalendarFeeds",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarFeeds_UpdatedByUserId",
                table: "CalendarFeeds",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Files_CreatedByUserId",
                table: "Files",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Files_ResourceId",
                table: "Files",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Files_SpaceId",
                table: "Files",
                column: "SpaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Files_StorageKey",
                table: "Files",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_DeduplicationKey",
                table: "Notifications",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId",
                table: "Notifications",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ReservationId_ReservationSeriesId",
                table: "Notifications",
                columns: new[] { "ReservationId", "ReservationSeriesId" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ReservationSeriesId",
                table: "Notifications",
                column: "ReservationSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_SentAt_NextAttemptAt",
                table: "Notifications",
                columns: new[] { "SentAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationEvents_ActorUserId",
                table: "ReservationEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationEvents_ReservationId_ReservationRevision",
                table: "ReservationEvents",
                columns: new[] { "ReservationId", "ReservationRevision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_BillingStatus_BillingDueAt",
                table: "Reservations",
                columns: new[] { "BillingStatus", "BillingDueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_DecidedByUserId",
                table: "Reservations",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_PaymentsInvoiceId",
                table: "Reservations",
                column: "PaymentsInvoiceId",
                unique: true,
                filter: "[PaymentsInvoiceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_ReservationSeriesId_OccurrenceNumber",
                table: "Reservations",
                columns: new[] { "ReservationSeriesId", "OccurrenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_ShareToken",
                table: "Reservations",
                column: "ShareToken",
                unique: true,
                filter: "[ShareToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Status_StartsAt",
                table: "Reservations",
                columns: new[] { "Status", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationSeries_CreatedByUserId",
                table: "ReservationSeries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationSeries_RequesterUserId_CreatedAt",
                table: "ReservationSeries",
                columns: new[] { "RequesterUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationSeries_ResourceConfigId_ResourceId",
                table: "ReservationSeries",
                columns: new[] { "ResourceConfigId", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationSeries_ResourceId_CreatedAt",
                table: "ReservationSeries",
                columns: new[] { "ResourceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationSeries_ShareToken",
                table: "ReservationSeries",
                column: "ShareToken",
                unique: true,
                filter: "[ShareToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationSeries_SubmissionKey",
                table: "ReservationSeries",
                column: "SubmissionKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceConfigs_CreatedByUserId",
                table: "ResourceConfigs",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceConfigs_PublishedByUserId",
                table: "ResourceConfigs",
                column: "PublishedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceConfigs_ResourceId_Version",
                table: "ResourceConfigs",
                columns: new[] { "ResourceId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceConfigs_SourceTemplateId",
                table: "ResourceConfigs",
                column: "SourceTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_ParentResourceId",
                table: "Resources",
                column: "ParentResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_ParentResourceId_SpaceId_TeamId",
                table: "Resources",
                columns: new[] { "ParentResourceId", "SpaceId", "TeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_Resources_SpaceId_ParentResourceId_NameKey",
                table: "Resources",
                columns: new[] { "SpaceId", "ParentResourceId", "NameKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Resources_SpaceId_Slug",
                table: "Resources",
                columns: new[] { "SpaceId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Resources_TeamId_SpaceId",
                table: "Resources",
                columns: new[] { "TeamId", "SpaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Resources_UpdatedByUserId",
                table: "Resources",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceTemplates_TeamId",
                table: "ResourceTemplates",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceTemplates_UpdatedByUserId",
                table: "ResourceTemplates",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleExceptions_CreatedByUserId",
                table: "ScheduleExceptions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleExceptions_ResourceId_LocalDate",
                table: "ScheduleExceptions",
                columns: new[] { "ResourceId", "LocalDate" },
                unique: true,
                filter: "[ResourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleExceptions_TeamId_LocalDate",
                table: "ScheduleExceptions",
                columns: new[] { "TeamId", "LocalDate" },
                unique: true,
                filter: "[TeamId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Spaces_ReferenceNumber",
                table: "Spaces",
                column: "ReferenceNumber",
                unique: true,
                filter: "[IsOfficialFacility] = 1 AND [ReferenceNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Spaces_Slug",
                table: "Spaces",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamPermissions_UserId",
                table: "TeamPermissions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Slug",
                table: "Teams",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamSpaces_SpaceId",
                table: "TeamSpaces",
                column: "SpaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_IamId",
                table: "Users",
                column: "IamId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarFeeds");

            migrationBuilder.DropTable(
                name: "Files");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "ReservationEvents");

            migrationBuilder.DropTable(
                name: "ScheduleExceptions");

            migrationBuilder.DropTable(
                name: "TeamPermissions");

            migrationBuilder.DropTable(
                name: "TeamSpaces");

            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropTable(
                name: "ReservationSeries");

            migrationBuilder.DropTable(
                name: "ResourceConfigs");

            migrationBuilder.DropTable(
                name: "ResourceTemplates");

            migrationBuilder.DropTable(
                name: "Resources");

            migrationBuilder.DropTable(
                name: "Spaces");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
