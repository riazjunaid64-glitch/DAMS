using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <summary>
    /// Where a booking-level customer credit lands on the installment schedule.
    /// <para>
    /// A balance reduction or credit note is entered against the booking, never against one
    /// installment, and until now it stopped there. The plan kept demanding the full price while the
    /// customer's balance said something smaller: the tail installment could be part-paid at most,
    /// Overdue over-stated the debt by exactly the rebate, and the sale could never be completed
    /// because completion requires every installment settled.
    /// </para>
    /// <para>
    /// These rows are bookkeeping, not a financial record — the money stays on the
    /// <c>RebateDisbursement</c> and its reversals, and <c>BookingCreditPolicy.ReallocateAsync</c>
    /// rebuilds them from those whenever anything moves. Hence the cascade delete: an allocation
    /// without its credit means nothing. The delete on Installments is deliberately NO ACTION so a
    /// schedule cannot be regenerated out from under a live allocation by accident; the service
    /// clears the rows first, then rebuilds them against the new plan.
    /// </para>
    /// <para>
    /// The backfill below places the credits already sitting on live schedules, so bookings that
    /// predate this migration are corrected too rather than waiting for their next rebate movement.
    /// It is written as a cursor on purpose: the allocation is an ordered fill (newest credit first,
    /// latest installment first) and a set-based rewrite of that is far harder to prove right than
    /// it is to run once.
    /// </para>
    /// </summary>
    public partial class AddRebateCreditAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RebateCreditAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisbursementId = table.Column<int>(type: "int", nullable: false),
                    InstallmentId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RebateCreditAllocations", x => x.Id);
                    table.CheckConstraint("CK_RebateCreditAllocations_Positive", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_RebateCreditAllocations_Installments_InstallmentId",
                        column: x => x.InstallmentId,
                        principalTable: "Installments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RebateCreditAllocations_RebateDisbursements_DisbursementId",
                        column: x => x.DisbursementId,
                        principalTable: "RebateDisbursements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RebateCreditAllocations_DisbursementId_InstallmentId",
                table: "RebateCreditAllocations",
                columns: new[] { "DisbursementId", "InstallmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RebateCreditAllocations_InstallmentId",
                table: "RebateCreditAllocations",
                column: "InstallmentId");

            migrationBuilder.Sql(Backfill);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RebateCreditAllocations");
        }

        private const string Backfill = """
            DECLARE @BookingId int, @Placeable decimal(18,2);

            DECLARE booking_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT b.Id,
                       -- What the plan still demands, less what the customer still owes: everything
                       -- the two have in common cancels and this is the part of the booking-level
                       -- credit the schedule was never shrunk by. Zero when the plan was built after
                       -- the rebate, because the pool had already taken it out.
                       CASE WHEN c.NetCredit <
                                 ISNULL(s.ScheduleTotal, 0) + b.BookingAmountReceived + c.NetCredit
                                 - (b.AgreedSalePrice - b.DiscountAmount)
                            THEN c.NetCredit
                            ELSE ISNULL(s.ScheduleTotal, 0) + b.BookingAmountReceived + c.NetCredit
                                 - (b.AgreedSalePrice - b.DiscountAmount)
                       END
                FROM Bookings b
                CROSS APPLY (
                    SELECT NetCredit = ISNULL(SUM(d.Amount - ISNULL(v.Reversed, 0)), 0)
                    FROM RebateDisbursements d
                    JOIN CustomerRebates r ON r.Id = d.RebateId
                    OUTER APPLY (
                        SELECT Reversed = SUM(rv.Amount)
                        FROM RebateDisbursementReversals rv
                        WHERE rv.DisbursementId = d.Id
                    ) v
                    WHERE r.BookingId = b.Id AND d.InstallmentId IS NULL AND d.Method IN (0, 1, 3)
                ) c
                OUTER APPLY (
                    SELECT ScheduleTotal = SUM(i.Amount) FROM Installments i WHERE i.BookingId = b.Id
                ) s
                WHERE c.NetCredit > 0 AND s.ScheduleTotal IS NOT NULL;

            OPEN booking_cursor;
            FETCH NEXT FROM booking_cursor INTO @BookingId, @Placeable;

            WHILE @@FETCH_STATUS = 0
            BEGIN
                IF @Placeable > 0
                BEGIN
                    -- Room left on each installment: its amount less cash taken and less any credit
                    -- the operator already aimed at it.
                    DECLARE @Room TABLE (InstallmentId int PRIMARY KEY, Ord int, Room decimal(18,2));
                    DELETE FROM @Room;
                    INSERT INTO @Room (InstallmentId, Ord, Room)
                    SELECT i.Id,
                           ROW_NUMBER() OVER (ORDER BY i.DueDate DESC, i.SequenceNumber DESC, i.Id DESC),
                           i.Amount - ISNULL(p.Paid, 0) - ISNULL(dc.Credited, 0)
                    FROM Installments i
                    OUTER APPLY (
                        SELECT Paid = SUM(pm.Amount) FROM Payments pm
                        WHERE pm.InstallmentId = i.Id AND pm.Type = 1
                    ) p
                    OUTER APPLY (
                        SELECT Credited = SUM(d.Amount - ISNULL(v.Reversed, 0))
                        FROM RebateDisbursements d
                        OUTER APPLY (
                            SELECT Reversed = SUM(rv.Amount)
                            FROM RebateDisbursementReversals rv WHERE rv.DisbursementId = d.Id
                        ) v
                        WHERE d.InstallmentId = i.Id AND d.Method IN (0, 1, 3)
                    ) dc
                    WHERE i.BookingId = @BookingId;
                    DELETE FROM @Room WHERE Room <= 0;

                    -- Each booking-level credit, newest first: when only part of the total is
                    -- placeable it is the credit that arrived after the plan was fixed.
                    DECLARE @Credit TABLE (DisbursementId int PRIMARY KEY, Ord int, NetAmount decimal(18,2));
                    DELETE FROM @Credit;
                    INSERT INTO @Credit (DisbursementId, Ord, NetAmount)
                    SELECT d.Id,
                           ROW_NUMBER() OVER (ORDER BY d.AppliedAt DESC, d.Id DESC),
                           d.Amount - ISNULL(v.Reversed, 0)
                    FROM RebateDisbursements d
                    JOIN CustomerRebates r ON r.Id = d.RebateId
                    OUTER APPLY (
                        SELECT Reversed = SUM(rv.Amount)
                        FROM RebateDisbursementReversals rv WHERE rv.DisbursementId = d.Id
                    ) v
                    WHERE r.BookingId = @BookingId AND d.InstallmentId IS NULL AND d.Method IN (0, 1, 3);
                    DELETE FROM @Credit WHERE NetAmount <= 0;

                    DECLARE @CreditOrd int = 1, @CreditLeft decimal(18,2) = 0, @DisbursementId int = NULL;
                    DECLARE @RoomOrd int = 1, @RoomLeft decimal(18,2) = 0, @InstallmentId int = NULL;
                    DECLARE @Take decimal(18,2);

                    WHILE @Placeable > 0
                    BEGIN
                        IF @DisbursementId IS NULL OR @CreditLeft <= 0
                        BEGIN
                            SELECT TOP 1 @DisbursementId = DisbursementId, @CreditLeft = NetAmount
                            FROM @Credit WHERE Ord = @CreditOrd;
                            IF @@ROWCOUNT = 0 BREAK;
                            SET @CreditOrd = @CreditOrd + 1;
                            IF @CreditLeft > @Placeable SET @CreditLeft = @Placeable;
                        END

                        IF @InstallmentId IS NULL OR @RoomLeft <= 0
                        BEGIN
                            SELECT TOP 1 @InstallmentId = InstallmentId, @RoomLeft = Room
                            FROM @Room WHERE Ord = @RoomOrd;
                            IF @@ROWCOUNT = 0 BREAK;
                            SET @RoomOrd = @RoomOrd + 1;
                        END

                        SET @Take = CASE WHEN @CreditLeft < @RoomLeft THEN @CreditLeft ELSE @RoomLeft END;
                        IF @Take <= 0 BREAK;

                        MERGE RebateCreditAllocations AS target
                        USING (SELECT @DisbursementId AS DisbursementId, @InstallmentId AS InstallmentId) AS source
                        ON target.DisbursementId = source.DisbursementId
                           AND target.InstallmentId = source.InstallmentId
                        WHEN MATCHED THEN UPDATE SET Amount = target.Amount + @Take
                        WHEN NOT MATCHED THEN
                            INSERT (DisbursementId, InstallmentId, Amount)
                            VALUES (source.DisbursementId, source.InstallmentId, @Take);

                        SET @CreditLeft = @CreditLeft - @Take;
                        SET @RoomLeft = @RoomLeft - @Take;
                        SET @Placeable = @Placeable - @Take;
                    END
                END

                FETCH NEXT FROM booking_cursor INTO @BookingId, @Placeable;
            END

            CLOSE booking_cursor;
            DEALLOCATE booking_cursor;

            -- An installment is settled by cash, by a credit aimed at it, or by a share of a
            -- booking-level credit. Bring every status back in line now that the third exists.
            -- InstallmentStatus: Pending 0, Paid 1, PartiallyPaid 3 (Overdue 2 is derived on read,
            -- never stored). Numbers, not names, so this keeps meaning what it meant today.
            UPDATE i
            SET Status = CASE WHEN cov.Covered >= i.Amount THEN 1
                              WHEN cov.Covered > 0 THEN 3
                              ELSE 0 END,
                PaidAt = CASE WHEN cov.Covered >= i.Amount THEN i.PaidAt ELSE NULL END
            FROM Installments i
            CROSS APPLY (
                SELECT Covered =
                    ISNULL((SELECT SUM(pm.Amount) FROM Payments pm
                            WHERE pm.InstallmentId = i.Id AND pm.Type = 1), 0)
                  + ISNULL((SELECT SUM(d.Amount) FROM RebateDisbursements d
                            WHERE d.InstallmentId = i.Id AND d.Method IN (0, 1, 3)), 0)
                  - ISNULL((SELECT SUM(rv.Amount) FROM RebateDisbursementReversals rv
                            JOIN RebateDisbursements d ON d.Id = rv.DisbursementId
                            WHERE d.InstallmentId = i.Id AND d.Method IN (0, 1, 3)), 0)
                  + ISNULL((SELECT SUM(a.Amount) FROM RebateCreditAllocations a
                            WHERE a.InstallmentId = i.Id), 0)
            ) cov
            WHERE EXISTS (SELECT 1 FROM RebateCreditAllocations a WHERE a.InstallmentId = i.Id);
            """;
    }
}
