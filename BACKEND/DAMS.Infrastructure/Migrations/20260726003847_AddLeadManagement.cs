using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_BookingRequests_UnitId_Status'
                      AND object_id = OBJECT_ID(N'[dbo].[BookingRequests]')
                )
                BEGIN
                    DROP INDEX [IX_BookingRequests_UnitId_Status] ON [dbo].[BookingRequests];
                END
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH(N'[dbo].[Employees]', N'TeamId') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[Employees] ADD [TeamId] int NULL;
                END
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH(N'[dbo].[Employees]', N'UserId') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[Employees] ADD [UserId] int NULL;
                END
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH(N'[dbo].[BookingRequests]', N'LeadId') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[BookingRequests] ADD [LeadId] int NULL;
                END
                """);

            migrationBuilder.CreateTable(
                name: "LeadClosureReasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadClosureReasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeadSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CustomerSource = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ManagerEmployeeId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_Employees_ManagerEmployeeId",
                        column: x => x.ManagerEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Leads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NormalizedPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    WhatsappNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NormalizedWhatsapp = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PreferredContactMethod = table.Column<int>(type: "int", nullable: false),
                    PreferredContactTime = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LeadSourceId = table.Column<int>(type: "int", nullable: false),
                    SourceDetails = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CampaignName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CampaignReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AdReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExternalProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExternalLeadId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExternalFormReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExternalSubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IntegrationPayload = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IntegrationStatus = table.Column<int>(type: "int", nullable: false),
                    IntegrationError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    InterestedProjectId = table.Column<int>(type: "int", nullable: true),
                    InterestedUnitId = table.Column<int>(type: "int", nullable: true),
                    PropertyType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PreferredLocation = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BudgetMin = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    BudgetMax = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PurchaseIntent = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AssignedEmployeeId = table.Column<int>(type: "int", nullable: true),
                    AssignedTeamId = table.Column<int>(type: "int", nullable: true),
                    AssignmentState = table.Column<int>(type: "int", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AssignedByUserId = table.Column<int>(type: "int", nullable: true),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    Qualification = table.Column<int>(type: "int", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastActivitySummary = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    NextActionAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextActionSummary = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FirstContactAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastContactAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConvertedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConvertedByUserId = table.Column<int>(type: "int", nullable: true),
                    ConvertedCustomerId = table.Column<int>(type: "int", nullable: true),
                    ConvertedBookingId = table.Column<int>(type: "int", nullable: true),
                    ClosureReasonId = table.Column<int>(type: "int", nullable: true),
                    ClosureNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReactivateOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Leads_Bookings_ConvertedBookingId",
                        column: x => x.ConvertedBookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Leads_Customers_ConvertedCustomerId",
                        column: x => x.ConvertedCustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Leads_Employees_AssignedEmployeeId",
                        column: x => x.AssignedEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Leads_LeadClosureReasons_ClosureReasonId",
                        column: x => x.ClosureReasonId,
                        principalTable: "LeadClosureReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Leads_LeadSources_LeadSourceId",
                        column: x => x.LeadSourceId,
                        principalTable: "LeadSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Leads_Projects_InterestedProjectId",
                        column: x => x.InterestedProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Leads_Teams_AssignedTeamId",
                        column: x => x.AssignedTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Leads_Units_InterestedUnitId",
                        column: x => x.InterestedUnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LeadActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Channel = table.Column<int>(type: "int", nullable: true),
                    PreviousValue = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CommunicationId = table.Column<int>(type: "int", nullable: true),
                    FollowUpId = table.Column<int>(type: "int", nullable: true),
                    SiteVisitId = table.Column<int>(type: "int", nullable: true),
                    DocumentId = table.Column<int>(type: "int", nullable: true),
                    CommentId = table.Column<int>(type: "int", nullable: true),
                    BookingId = table.Column<int>(type: "int", nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    PerformedByUserId = table.Column<int>(type: "int", nullable: true),
                    PerformedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsSystemGenerated = table.Column<bool>(type: "bit", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadActivities_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeadAssignmentHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    PreviousEmployeeId = table.Column<int>(type: "int", nullable: true),
                    PreviousTeamId = table.Column<int>(type: "int", nullable: true),
                    AssignedEmployeeId = table.Column<int>(type: "int", nullable: true),
                    AssignedTeamId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AssignedByUserId = table.Column<int>(type: "int", nullable: true),
                    AssignedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadAssignmentHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadAssignmentHistories_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeadComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    ParentCommentId = table.Column<int>(type: "int", nullable: true),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    IsManagerReviewRequest = table.Column<bool>(type: "bit", nullable: false),
                    IsDecisionRecord = table.Column<bool>(type: "bit", nullable: false),
                    AuthorUserId = table.Column<int>(type: "int", nullable: false),
                    AuthorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadComments_LeadComments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "LeadComments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeadComments_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeadCommunications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CustomerResponse = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NextAction = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NextActionAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExternalProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExternalMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadCommunications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadCommunications_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeadCommunications_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeadExternalSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalLeadId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExternalFormReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExternalSubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadExternalSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadExternalSubmissions_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeadFollowUps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    AssignedEmployeeId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DueAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RemindAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CompletedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadFollowUps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadFollowUps_Employees_AssignedEmployeeId",
                        column: x => x.AssignedEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeadFollowUps_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeadNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    RecipientUserId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DedupKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsEscalation = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadNotifications_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeadNotifications_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "LeadSiteVisits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: true),
                    UnitId = table.Column<int>(type: "int", nullable: true),
                    AssignedEmployeeId = table.Column<int>(type: "int", nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MeetingLocation = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CustomerAttendees = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    InternalAttendees = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RemindAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: true),
                    OutcomeNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CustomerFeedback = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NextAction = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OriginalScheduledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RescheduleCount = table.Column<int>(type: "int", nullable: false),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadSiteVisits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadSiteVisits_Employees_AssignedEmployeeId",
                        column: x => x.AssignedEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeadSiteVisits_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeadSiteVisits_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LeadSiteVisits_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LeadCommentMentions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadCommentId = table.Column<int>(type: "int", nullable: false),
                    MentionedUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadCommentMentions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadCommentMentions_LeadComments_LeadCommentId",
                        column: x => x.LeadCommentId,
                        principalTable: "LeadComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeadCommentMentions_Users_MentionedUserId",
                        column: x => x.MentionedUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "LeadDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    CommunicationId = table.Column<int>(type: "int", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: true),
                    UploadedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadDocuments_LeadCommunications_CommunicationId",
                        column: x => x.CommunicationId,
                        principalTable: "LeadCommunications",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeadDocuments_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "LeadClosureReasons",
                columns: new[] { "Id", "Code", "CreatedAt", "DisplayOrder", "IsActive", "IsSystem", "Kind", "Name", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, "budget_issue", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1, true, true, 2, "Budget issue", null },
                    { 2, "not_interested", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 2, true, true, 0, "Not interested", null },
                    { 3, "purchased_elsewhere", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 3, true, true, 0, "Purchased elsewhere", null },
                    { 4, "location_unsuitable", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 4, true, true, 0, "Location unsuitable", null },
                    { 5, "payment_plan_unsuitable", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 5, true, true, 2, "Payment plan unsuitable", null },
                    { 6, "unable_to_contact", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 6, true, true, 2, "Unable to contact", null },
                    { 7, "invalid_information", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 7, true, true, 0, "Invalid information", null },
                    { 8, "duplicate", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 8, true, true, 0, "Duplicate", null },
                    { 9, "delayed_decision", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 9, true, true, 1, "Delayed decision", null },
                    { 10, "other", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 10, true, true, 2, "Other", null }
                });

            migrationBuilder.InsertData(
                table: "LeadSources",
                columns: new[] { "Id", "Code", "CreatedAt", "CustomerSource", "DisplayOrder", "IsActive", "IsSystem", "Name", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, "manual", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 4, 1, true, true, "Manual Entry", null },
                    { 2, "walk_in", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1, 2, true, true, "Office Walk-in", null },
                    { 3, "phone", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 2, 3, true, true, "Phone Call", null },
                    { 4, "referral", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 3, 4, true, true, "Referral", null },
                    { 5, "website", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 0, 5, true, true, "Website Inquiry", null },
                    { 6, "facebook", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 4, 6, true, true, "Facebook", null },
                    { 7, "instagram", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 4, 7, true, true, "Instagram", null },
                    { 8, "whatsapp", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 4, 8, true, true, "WhatsApp", null },
                    { 9, "property_portal", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 4, 9, true, true, "Property Portal", null },
                    { 10, "broker", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 3, 10, true, true, "Broker / Agent", null },
                    { 11, "campaign", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 4, 11, true, true, "Marketing Campaign", null },
                    { 12, "exhibition", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 4, 12, true, true, "Exhibition / Event", null },
                    { 13, "other", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), 4, 13, true, true, "Other", null }
                });

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [RoleId] = 3)
                BEGIN
                    INSERT INTO [dbo].[Roles] ([RoleId], [Role_name]) VALUES (3, N'Manager');
                END

                IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [RoleId] = 4)
                BEGIN
                    INSERT INTO [dbo].[Roles] ([RoleId], [Role_name]) VALUES (4, N'Employee');
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_Employees_TeamId'
                      AND object_id = OBJECT_ID(N'[dbo].[Employees]')
                )
                BEGIN
                    CREATE INDEX [IX_Employees_TeamId] ON [dbo].[Employees] ([TeamId]);
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_Employees_UserId'
                      AND object_id = OBJECT_ID(N'[dbo].[Employees]')
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_Employees_UserId] ON [dbo].[Employees] ([UserId]) WHERE [UserId] IS NOT NULL;
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_BookingRequests_LeadId'
                      AND object_id = OBJECT_ID(N'[dbo].[BookingRequests]')
                )
                BEGIN
                    CREATE INDEX [IX_BookingRequests_LeadId] ON [dbo].[BookingRequests] ([LeadId]);
                END
                """);

            migrationBuilder.CreateIndex(
                name: "IX_LeadActivities_LeadId_OccurredAt",
                table: "LeadActivities",
                columns: new[] { "LeadId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadActivities_Type",
                table: "LeadActivities",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_LeadAssignmentHistories_LeadId_AssignedAt",
                table: "LeadAssignmentHistories",
                columns: new[] { "LeadId", "AssignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadClosureReasons_Code",
                table: "LeadClosureReasons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadCommentMentions_LeadCommentId_MentionedUserId",
                table: "LeadCommentMentions",
                columns: new[] { "LeadCommentId", "MentionedUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadCommentMentions_MentionedUserId",
                table: "LeadCommentMentions",
                column: "MentionedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadComments_LeadId_CreatedAt",
                table: "LeadComments",
                columns: new[] { "LeadId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadComments_ParentCommentId",
                table: "LeadComments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadCommunications_EmployeeId",
                table: "LeadCommunications",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadCommunications_ExternalProvider_ExternalMessageId",
                table: "LeadCommunications",
                columns: new[] { "ExternalProvider", "ExternalMessageId" },
                unique: true,
                filter: "[ExternalProvider] IS NOT NULL AND [ExternalMessageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LeadCommunications_LeadId_OccurredAt",
                table: "LeadCommunications",
                columns: new[] { "LeadId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadDocuments_CommunicationId",
                table: "LeadDocuments",
                column: "CommunicationId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadDocuments_LeadId_UploadedAt",
                table: "LeadDocuments",
                columns: new[] { "LeadId", "UploadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadDocuments_StoredFileName",
                table: "LeadDocuments",
                column: "StoredFileName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadExternalSubmissions_LeadId_ReceivedAt",
                table: "LeadExternalSubmissions",
                columns: new[] { "LeadId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadExternalSubmissions_Provider_ExternalLeadId",
                table: "LeadExternalSubmissions",
                columns: new[] { "Provider", "ExternalLeadId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadFollowUps_AssignedEmployeeId_Status_DueAt",
                table: "LeadFollowUps",
                columns: new[] { "AssignedEmployeeId", "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadFollowUps_LeadId_Status",
                table: "LeadFollowUps",
                columns: new[] { "LeadId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadFollowUps_Status_DueAt",
                table: "LeadFollowUps",
                columns: new[] { "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadNotifications_DedupKey",
                table: "LeadNotifications",
                column: "DedupKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadNotifications_LeadId",
                table: "LeadNotifications",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadNotifications_RecipientUserId_IsRead_CreatedAt",
                table: "LeadNotifications",
                columns: new[] { "RecipientUserId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_AssignedEmployeeId_Stage",
                table: "Leads",
                columns: new[] { "AssignedEmployeeId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_AssignedTeamId_Stage",
                table: "Leads",
                columns: new[] { "AssignedTeamId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_ClosureReasonId",
                table: "Leads",
                column: "ClosureReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_ConvertedBookingId",
                table: "Leads",
                column: "ConvertedBookingId",
                unique: true,
                filter: "[ConvertedBookingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_ConvertedCustomerId",
                table: "Leads",
                column: "ConvertedCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_CreatedAt",
                table: "Leads",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_ExternalProvider_ExternalLeadId",
                table: "Leads",
                columns: new[] { "ExternalProvider", "ExternalLeadId" },
                unique: true,
                filter: "[ExternalProvider] IS NOT NULL AND [ExternalLeadId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_InterestedProjectId",
                table: "Leads",
                column: "InterestedProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_InterestedUnitId",
                table: "Leads",
                column: "InterestedUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_LastActivityAt",
                table: "Leads",
                column: "LastActivityAt");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_LeadReference",
                table: "Leads",
                column: "LeadReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Leads_LeadSourceId",
                table: "Leads",
                column: "LeadSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_NextActionAt",
                table: "Leads",
                column: "NextActionAt");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_NormalizedEmail",
                table: "Leads",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_NormalizedPhone",
                table: "Leads",
                column: "NormalizedPhone");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_NormalizedWhatsapp",
                table: "Leads",
                column: "NormalizedWhatsapp");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_Stage",
                table: "Leads",
                column: "Stage");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_Stage_CreatedAt",
                table: "Leads",
                columns: new[] { "Stage", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadSiteVisits_AssignedEmployeeId_Status_ScheduledAt",
                table: "LeadSiteVisits",
                columns: new[] { "AssignedEmployeeId", "Status", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadSiteVisits_LeadId_ScheduledAt",
                table: "LeadSiteVisits",
                columns: new[] { "LeadId", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadSiteVisits_ProjectId",
                table: "LeadSiteVisits",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadSiteVisits_Status_ScheduledAt",
                table: "LeadSiteVisits",
                columns: new[] { "Status", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadSiteVisits_UnitId",
                table: "LeadSiteVisits",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadSources_Code",
                table: "LeadSources",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_ManagerEmployeeId",
                table: "Teams",
                column: "ManagerEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Name",
                table: "Teams",
                column: "Name",
                unique: true);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BookingRequests_Leads_LeadId')
                BEGIN
                    ALTER TABLE [dbo].[BookingRequests] ADD CONSTRAINT [FK_BookingRequests_Leads_LeadId]
                        FOREIGN KEY ([LeadId]) REFERENCES [dbo].[Leads] ([Id]);
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Employees_Teams_TeamId')
                BEGIN
                    ALTER TABLE [dbo].[Employees] ADD CONSTRAINT [FK_Employees_Teams_TeamId]
                        FOREIGN KEY ([TeamId]) REFERENCES [dbo].[Teams] ([Id]) ON DELETE SET NULL;
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Employees_Users_UserId')
                BEGIN
                    ALTER TABLE [dbo].[Employees] ADD CONSTRAINT [FK_Employees_Users_UserId]
                        FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([UserId]) ON DELETE SET NULL;
                END
                """);

            // Units held at PendingReview by the old enquiry flow are released back to the
            // market: a website enquiry now creates a lead instead of reserving stock.
            // Units with a live booking are left alone.
            migrationBuilder.Sql("""
                UPDATE [Units]
                SET [Status] = 'Available'
                WHERE [Status] = 'PendingReview'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [Bookings] AS [b]
                      WHERE [b].[UnitId] = [Units].[Id]
                        AND [b].[Status] <> 4
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookingRequests_Leads_LeadId",
                table: "BookingRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Teams_TeamId",
                table: "Employees");

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Users_UserId",
                table: "Employees");

            migrationBuilder.DropTable(
                name: "LeadActivities");

            migrationBuilder.DropTable(
                name: "LeadAssignmentHistories");

            migrationBuilder.DropTable(
                name: "LeadCommentMentions");

            migrationBuilder.DropTable(
                name: "LeadDocuments");

            migrationBuilder.DropTable(
                name: "LeadExternalSubmissions");

            migrationBuilder.DropTable(
                name: "LeadFollowUps");

            migrationBuilder.DropTable(
                name: "LeadNotifications");

            migrationBuilder.DropTable(
                name: "LeadSiteVisits");

            migrationBuilder.DropTable(
                name: "LeadComments");

            migrationBuilder.DropTable(
                name: "LeadCommunications");

            migrationBuilder.DropTable(
                name: "Leads");

            migrationBuilder.DropTable(
                name: "LeadClosureReasons");

            migrationBuilder.DropTable(
                name: "LeadSources");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Employees_TeamId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Employees_UserId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_BookingRequests_LeadId",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "LeadId",
                table: "BookingRequests");

            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_UnitId_Status",
                table: "BookingRequests",
                columns: new[] { "UnitId", "Status" },
                unique: true,
                filter: "[Status] = 0");
        }
    }
}
