using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCommissionRebateManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerRebates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    CalculationType = table.Column<int>(type: "int", nullable: false),
                    PercentageRate = table.Column<decimal>(type: "decimal(9,6)", nullable: true),
                    FixedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CalculationBasis = table.Column<int>(type: "int", nullable: false),
                    BasisAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CalculatedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AdjustmentAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AdjustmentReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FinalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedByUserId = table.Column<int>(type: "int", nullable: true),
                    SubmittedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionByUserId = table.Column<int>(type: "int", nullable: true),
                    DecisionByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DecisionAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CancellationOrReversalReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerRebates", x => x.Id);
                    table.CheckConstraint("CK_CustomerRebates_Amounts", "[BasisAmount] > 0 AND [CalculatedAmount] >= 0 AND [FinalAmount] >= 0 AND ([ApprovedAmount] IS NULL OR [ApprovedAmount] >= 0)");
                    table.ForeignKey(
                        name: "FK_CustomerRebates_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerRebates_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ThirdPartyPartners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PartnerType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ContactPerson = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NormalizedPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Cnic = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NormalizedCnic = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Ntn = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    NormalizedNtn = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    RegistrationNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InternalCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    AccountTitle = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    AccountNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Iban = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThirdPartyPartners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RebateDisbursements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RebateId = table.Column<int>(type: "int", nullable: false),
                    FinanceAccountId = table.Column<int>(type: "int", nullable: true),
                    InstallmentId = table.Column<int>(type: "int", nullable: true),
                    Method = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaymentMethod = table.Column<int>(type: "int", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: true),
                    RecordedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RebateDisbursements", x => x.Id);
                    table.CheckConstraint("CK_RebateDisbursements_Positive", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_RebateDisbursements_CustomerRebates_RebateId",
                        column: x => x.RebateId,
                        principalTable: "CustomerRebates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RebateDisbursements_FinanceAccounts_FinanceAccountId",
                        column: x => x.FinanceAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RebateDisbursements_Installments_InstallmentId",
                        column: x => x.InstallmentId,
                        principalTable: "Installments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommissionRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PartnerId = table.Column<int>(type: "int", nullable: true),
                    PartnerType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ProjectId = table.Column<int>(type: "int", nullable: true),
                    UnitCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BookingSource = table.Column<int>(type: "int", nullable: true),
                    BookingId = table.Column<int>(type: "int", nullable: true),
                    CalculationType = table.Column<int>(type: "int", nullable: false),
                    PercentageRate = table.Column<decimal>(type: "decimal(9,6)", nullable: true),
                    FixedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CalculationBasis = table.Column<int>(type: "int", nullable: false),
                    MinimumCommission = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MaximumCommission = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    EligibilityCondition = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EarningCondition = table.Column<int>(type: "int", nullable: false),
                    MinimumCollectionPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    RequiresApproval = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionRules", x => x.Id);
                    table.CheckConstraint("CK_CommissionRules_Calculation", "([CalculationType] = 0 AND [PercentageRate] IS NOT NULL AND [PercentageRate] > 0 AND [FixedAmount] IS NULL) OR ([CalculationType] = 1 AND [FixedAmount] IS NOT NULL AND [FixedAmount] > 0 AND [PercentageRate] IS NULL)");
                    table.ForeignKey(
                        name: "FK_CommissionRules_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissionRules_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissionRules_ThirdPartyPartners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "ThirdPartyPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ThirdPartyAttributions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartnerId = table.Column<int>(type: "int", nullable: false),
                    LeadId = table.Column<int>(type: "int", nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    BookingId = table.Column<int>(type: "int", nullable: true),
                    RelationshipType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IntroducedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SourceDetails = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    AllocationPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    AssignedByUserId = table.Column<int>(type: "int", nullable: true),
                    AssignedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThirdPartyAttributions", x => x.Id);
                    table.CheckConstraint("CK_ThirdPartyAttributions_Allocation", "[AllocationPercent] > 0 AND [AllocationPercent] <= 100");
                    table.CheckConstraint("CK_ThirdPartyAttributions_ExactlyOneOwner", "([LeadId] IS NOT NULL AND [CustomerId] IS NULL AND [BookingId] IS NULL) OR ([LeadId] IS NULL AND [CustomerId] IS NOT NULL AND [BookingId] IS NULL) OR ([LeadId] IS NULL AND [CustomerId] IS NULL AND [BookingId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ThirdPartyAttributions_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ThirdPartyAttributions_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ThirdPartyAttributions_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ThirdPartyAttributions_ThirdPartyPartners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "ThirdPartyPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RebateDisbursementReversals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisbursementId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ReversedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReversedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReversedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RebateDisbursementReversals", x => x.Id);
                    table.CheckConstraint("CK_RebateDisbursementReversals_Positive", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_RebateDisbursementReversals_RebateDisbursements_DisbursementId",
                        column: x => x.DisbursementId,
                        principalTable: "RebateDisbursements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookingCommissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    PartnerId = table.Column<int>(type: "int", nullable: false),
                    AttributionId = table.Column<int>(type: "int", nullable: true),
                    RuleId = table.Column<int>(type: "int", nullable: true),
                    IsManual = table.Column<bool>(type: "bit", nullable: false),
                    ManualReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PartnerNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PartnerTypeSnapshot = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    PartnerInternalCodeSnapshot = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AllocationPercentSnapshot = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    RuleNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RulePrioritySnapshot = table.Column<int>(type: "int", nullable: true),
                    MinimumCommissionSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MaximumCommissionSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    EligibilityConditionSnapshot = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequiresApprovalSnapshot = table.Column<bool>(type: "bit", nullable: false),
                    CalculationType = table.Column<int>(type: "int", nullable: false),
                    PercentageRate = table.Column<decimal>(type: "decimal(9,6)", nullable: true),
                    FixedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CalculationBasis = table.Column<int>(type: "int", nullable: false),
                    BasisAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CalculatedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AdjustmentAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AdjustmentReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FinalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    EarningCondition = table.Column<int>(type: "int", nullable: false),
                    MinimumCollectionPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedByUserId = table.Column<int>(type: "int", nullable: true),
                    SubmittedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionByUserId = table.Column<int>(type: "int", nullable: true),
                    DecisionByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DecisionAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EarnedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PayableAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationOrReversalReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingCommissions", x => x.Id);
                    table.CheckConstraint("CK_BookingCommissions_Amounts", "[BasisAmount] > 0 AND [CalculatedAmount] >= 0 AND [FinalAmount] >= 0 AND ([ApprovedAmount] IS NULL OR [ApprovedAmount] >= 0) AND [AllocationPercentSnapshot] > 0 AND [AllocationPercentSnapshot] <= 100");
                    table.ForeignKey(
                        name: "FK_BookingCommissions_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingCommissions_CommissionRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "CommissionRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingCommissions_ThirdPartyAttributions_AttributionId",
                        column: x => x.AttributionId,
                        principalTable: "ThirdPartyAttributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingCommissions_ThirdPartyPartners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "ThirdPartyPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommissionPayouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CommissionId = table.Column<int>(type: "int", nullable: false),
                    FinanceAccountId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaymentMethod = table.Column<int>(type: "int", nullable: false),
                    PaymentReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DestinationBankNameSnapshot = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DestinationAccountTitleSnapshot = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DestinationAccountNumberSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DestinationIbanSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: true),
                    RecordedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionPayouts", x => x.Id);
                    table.CheckConstraint("CK_CommissionPayouts_Positive", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_CommissionPayouts_BookingCommissions_CommissionId",
                        column: x => x.CommissionId,
                        principalTable: "BookingCommissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissionPayouts_FinanceAccounts_FinanceAccountId",
                        column: x => x.FinanceAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommissionPayoutReversals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PayoutId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ReversedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReversedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReversedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionPayoutReversals", x => x.Id);
                    table.CheckConstraint("CK_CommissionPayoutReversals_Positive", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_CommissionPayoutReversals_CommissionPayouts_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "CommissionPayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialEvidence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CommissionId = table.Column<int>(type: "int", nullable: true),
                    PayoutId = table.Column<int>(type: "int", nullable: true),
                    RebateId = table.Column<int>(type: "int", nullable: true),
                    RebateDisbursementId = table.Column<int>(type: "int", nullable: true),
                    StoredFileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: true),
                    UploadedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialEvidence", x => x.Id);
                    table.CheckConstraint("CK_FinancialEvidence_ExactlyOneOwner", "(CASE WHEN [CommissionId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [PayoutId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [RebateId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [RebateDisbursementId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_FinancialEvidence_BookingCommissions_CommissionId",
                        column: x => x.CommissionId,
                        principalTable: "BookingCommissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialEvidence_CommissionPayouts_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "CommissionPayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialEvidence_CustomerRebates_RebateId",
                        column: x => x.RebateId,
                        principalTable: "CustomerRebates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialEvidence_RebateDisbursements_RebateDisbursementId",
                        column: x => x.RebateDisbursementId,
                        principalTable: "RebateDisbursements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialWorkflowAuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartnerId = table.Column<int>(type: "int", nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    BookingId = table.Column<int>(type: "int", nullable: true),
                    CommissionRuleId = table.Column<int>(type: "int", nullable: true),
                    CommissionId = table.Column<int>(type: "int", nullable: true),
                    PayoutId = table.Column<int>(type: "int", nullable: true),
                    RebateId = table.Column<int>(type: "int", nullable: true),
                    RebateDisbursementId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<int>(type: "int", nullable: false),
                    PreviousCommissionStatus = table.Column<int>(type: "int", nullable: true),
                    NewCommissionStatus = table.Column<int>(type: "int", nullable: true),
                    PreviousRebateStatus = table.Column<int>(type: "int", nullable: true),
                    NewRebateStatus = table.Column<int>(type: "int", nullable: true),
                    PreviousAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    NewAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PerformedByUserId = table.Column<int>(type: "int", nullable: true),
                    PerformedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialWorkflowAuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialWorkflowAuditEntries_BookingCommissions_CommissionId",
                        column: x => x.CommissionId,
                        principalTable: "BookingCommissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialWorkflowAuditEntries_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialWorkflowAuditEntries_CommissionPayouts_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "CommissionPayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialWorkflowAuditEntries_CommissionRules_CommissionRuleId",
                        column: x => x.CommissionRuleId,
                        principalTable: "CommissionRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialWorkflowAuditEntries_CustomerRebates_RebateId",
                        column: x => x.RebateId,
                        principalTable: "CustomerRebates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialWorkflowAuditEntries_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialWorkflowAuditEntries_RebateDisbursements_RebateDisbursementId",
                        column: x => x.RebateDisbursementId,
                        principalTable: "RebateDisbursements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialWorkflowAuditEntries_ThirdPartyPartners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "ThirdPartyPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingCommissions_AttributionId",
                table: "BookingCommissions",
                column: "AttributionId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingCommissions_BookingId_PartnerId",
                table: "BookingCommissions",
                columns: new[] { "BookingId", "PartnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingCommissions_PartnerId",
                table: "BookingCommissions",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingCommissions_RuleId",
                table: "BookingCommissions",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingCommissions_Status_CreatedAt",
                table: "BookingCommissions",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayoutReversals_IdempotencyKey",
                table: "CommissionPayoutReversals",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayoutReversals_PayoutId_ReversedAt",
                table: "CommissionPayoutReversals",
                columns: new[] { "PayoutId", "ReversedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_CommissionId_PaymentDate",
                table: "CommissionPayouts",
                columns: new[] { "CommissionId", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_FinanceAccountId",
                table: "CommissionPayouts",
                column: "FinanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_IdempotencyKey",
                table: "CommissionPayouts",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_BookingId",
                table: "CommissionRules",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_IsActive_EffectiveFrom_EffectiveTo_Priority",
                table: "CommissionRules",
                columns: new[] { "IsActive", "EffectiveFrom", "EffectiveTo", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_PartnerId",
                table: "CommissionRules",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_ProjectId",
                table: "CommissionRules",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRebates_BookingId",
                table: "CustomerRebates",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRebates_CustomerId",
                table: "CustomerRebates",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRebates_Status_CreatedAt",
                table: "CustomerRebates",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEvidence_CommissionId",
                table: "FinancialEvidence",
                column: "CommissionId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEvidence_PayoutId",
                table: "FinancialEvidence",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEvidence_RebateDisbursementId",
                table: "FinancialEvidence",
                column: "RebateDisbursementId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEvidence_RebateId",
                table: "FinancialEvidence",
                column: "RebateId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEvidence_StoredFileName",
                table: "FinancialEvidence",
                column: "StoredFileName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialWorkflowAuditEntries_BookingId_OccurredAt",
                table: "FinancialWorkflowAuditEntries",
                columns: new[] { "BookingId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialWorkflowAuditEntries_CommissionId_OccurredAt",
                table: "FinancialWorkflowAuditEntries",
                columns: new[] { "CommissionId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialWorkflowAuditEntries_CommissionRuleId_OccurredAt",
                table: "FinancialWorkflowAuditEntries",
                columns: new[] { "CommissionRuleId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialWorkflowAuditEntries_CustomerId",
                table: "FinancialWorkflowAuditEntries",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialWorkflowAuditEntries_PartnerId_OccurredAt",
                table: "FinancialWorkflowAuditEntries",
                columns: new[] { "PartnerId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialWorkflowAuditEntries_PayoutId",
                table: "FinancialWorkflowAuditEntries",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialWorkflowAuditEntries_RebateDisbursementId",
                table: "FinancialWorkflowAuditEntries",
                column: "RebateDisbursementId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialWorkflowAuditEntries_RebateId_OccurredAt",
                table: "FinancialWorkflowAuditEntries",
                columns: new[] { "RebateId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RebateDisbursementReversals_DisbursementId_ReversedAt",
                table: "RebateDisbursementReversals",
                columns: new[] { "DisbursementId", "ReversedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RebateDisbursementReversals_IdempotencyKey",
                table: "RebateDisbursementReversals",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RebateDisbursements_FinanceAccountId",
                table: "RebateDisbursements",
                column: "FinanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RebateDisbursements_IdempotencyKey",
                table: "RebateDisbursements",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RebateDisbursements_InstallmentId",
                table: "RebateDisbursements",
                column: "InstallmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RebateDisbursements_RebateId_AppliedAt",
                table: "RebateDisbursements",
                columns: new[] { "RebateId", "AppliedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ThirdPartyAttributions_Booking_Primary",
                table: "ThirdPartyAttributions",
                column: "BookingId",
                unique: true,
                filter: "[BookingId] IS NOT NULL AND [IsPrimary] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ThirdPartyAttributions_BookingId_PartnerId",
                table: "ThirdPartyAttributions",
                columns: new[] { "BookingId", "PartnerId" },
                unique: true,
                filter: "[BookingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ThirdPartyAttributions_CustomerId_PartnerId",
                table: "ThirdPartyAttributions",
                columns: new[] { "CustomerId", "PartnerId" },
                unique: true,
                filter: "[CustomerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ThirdPartyAttributions_LeadId_PartnerId",
                table: "ThirdPartyAttributions",
                columns: new[] { "LeadId", "PartnerId" },
                unique: true,
                filter: "[LeadId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ThirdPartyAttributions_PartnerId",
                table: "ThirdPartyAttributions",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ThirdPartyPartners_InternalCode",
                table: "ThirdPartyPartners",
                column: "InternalCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ThirdPartyPartners_IsActive_PartnerType",
                table: "ThirdPartyPartners",
                columns: new[] { "IsActive", "PartnerType" });

            migrationBuilder.CreateIndex(
                name: "IX_ThirdPartyPartners_NormalizedCnic",
                table: "ThirdPartyPartners",
                column: "NormalizedCnic",
                unique: true,
                filter: "[NormalizedCnic] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ThirdPartyPartners_NormalizedEmail",
                table: "ThirdPartyPartners",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_ThirdPartyPartners_NormalizedNtn",
                table: "ThirdPartyPartners",
                column: "NormalizedNtn",
                unique: true,
                filter: "[NormalizedNtn] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ThirdPartyPartners_NormalizedPhone",
                table: "ThirdPartyPartners",
                column: "NormalizedPhone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommissionPayoutReversals");

            migrationBuilder.DropTable(
                name: "FinancialEvidence");

            migrationBuilder.DropTable(
                name: "FinancialWorkflowAuditEntries");

            migrationBuilder.DropTable(
                name: "RebateDisbursementReversals");

            migrationBuilder.DropTable(
                name: "CommissionPayouts");

            migrationBuilder.DropTable(
                name: "RebateDisbursements");

            migrationBuilder.DropTable(
                name: "BookingCommissions");

            migrationBuilder.DropTable(
                name: "CustomerRebates");

            migrationBuilder.DropTable(
                name: "CommissionRules");

            migrationBuilder.DropTable(
                name: "ThirdPartyAttributions");

            migrationBuilder.DropTable(
                name: "ThirdPartyPartners");
        }
    }
}
