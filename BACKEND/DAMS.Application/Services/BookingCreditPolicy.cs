using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services;

/// <summary>One source of truth for non-cash credits that reduce a booking receivable.</summary>
internal static class BookingCreditPolicy
{
    /// <summary>The three delivery methods that hand the customer a credit instead of cash.</summary>
    public static bool IsNonCashCredit(CustomerRebateMethod method) =>
        method is CustomerRebateMethod.OutstandingBalanceReduction
            or CustomerRebateMethod.InstallmentAdjustment
            or CustomerRebateMethod.CreditNote;

    public static async Task<decimal> GetNonCashCreditsAsync(
        AppDbContext context,
        int bookingId,
        CancellationToken cancellationToken = default)
    {
        var applied = await context.RebateDisbursements
            .Where(d => d.Rebate.BookingId == bookingId
                && (d.Method == CustomerRebateMethod.OutstandingBalanceReduction
                    || d.Method == CustomerRebateMethod.InstallmentAdjustment
                    || d.Method == CustomerRebateMethod.CreditNote))
            .SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m;
        var reversed = await context.RebateDisbursementReversals
            .Where(r => r.Disbursement.Rebate.BookingId == bookingId
                && (r.Disbursement.Method == CustomerRebateMethod.OutstandingBalanceReduction
                    || r.Disbursement.Method == CustomerRebateMethod.InstallmentAdjustment
                    || r.Disbursement.Method == CustomerRebateMethod.CreditNote))
            .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;
        return Money(applied - reversed);
    }

    // A non-cash credit (balance reduction / credit note / installment adjustment) counts directly
    // toward the booking-amount milestone: it substitutes for the cash the customer would otherwise
    // pay, so the remaining cash required drops by the credit and can reach zero. Never negative, and
    // never more than the original requirement.
    public static decimal EffectiveBookingAmountRequired(Booking booking, decimal nonCashCredits) =>
        Money(Math.Max(0m, booking.BookingAmountRequired - Math.Max(0m, nonCashCredits)));

    public static decimal RemainingInstallmentPool(Booking booking, decimal nonCashCredits) =>
        Money(Math.Max(0m, booking.AgreedSalePrice - booking.DiscountAmount
            - booking.BookingAmountReceived - nonCashCredits - booking.PossessionAmount));

    /// <summary>
    /// Lands every booking-level customer credit on the installment schedule, and brings each
    /// installment's status back in line with what now covers it.
    /// <para>
    /// A balance reduction or credit note is entered against the booking as a whole. The schedule is
    /// the only route DAMS has for collecting a receivable, so a credit that never reaches it leaves
    /// the plan demanding the full price while the customer's balance says something smaller: the
    /// tail installment can be part-paid at most, the sale can never be completed, and Overdue
    /// over-states the debt by exactly the rebate. Allocating it here is what keeps
    /// "schedule remaining" and "amount still owed" the same number.
    /// </para>
    /// <para>
    /// The allocation is REBUILT from the disbursement and reversal rows every time, never adjusted
    /// in place, so it cannot drift from the money it represents and a partial reversal needs no
    /// special case. Credits fill the latest installments first: the concession comes off the end of
    /// the plan, leaving the dates the customer already agreed to intact. Allocations are read only
    /// by the per-installment views — the booking-level credit total still comes from the
    /// disbursements themselves, so nothing is counted twice.
    /// </para>
    /// <para>
    /// ONLY the part of a credit the plan is not already short by is placed, which is what keeps a
    /// rebate affecting the customer exactly once. <see cref="RemainingInstallmentPool"/> takes the
    /// credit out of a schedule built AFTER the rebate arrived; allocating it there as well would
    /// take it off twice and leave the plan collecting less than the sale. So the placeable amount
    /// is measured, not assumed: it is the gap between what the plan still demands and what the
    /// customer still owes, and it comes out at zero for a plan that already absorbed the credit.
    /// </para>
    /// <para>Call inside the caller's transaction, after the credit rows themselves are saved.</para>
    /// </summary>
    public static async Task ReallocateAsync(
        AppDbContext context,
        int bookingId,
        DateTime settledAt,
        CancellationToken cancellationToken = default)
    {
        var booking = await context.Bookings.SingleOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
        if (booking is null) return;

        var installments = await context.Installments
            .Where(i => i.BookingId == bookingId)
            .ToListAsync(cancellationToken);

        // Every credit on this booking, with what has since been reversed out of it. Installment
        // adjustments already name their target and are left exactly where the operator put them;
        // only the booking-level ones need placing.
        var credits = await context.RebateDisbursements
            .Where(d => d.Rebate.BookingId == bookingId
                && (d.Method == CustomerRebateMethod.OutstandingBalanceReduction
                    || d.Method == CustomerRebateMethod.InstallmentAdjustment
                    || d.Method == CustomerRebateMethod.CreditNote))
            .Select(d => new
            {
                d.Id,
                d.InstallmentId,
                d.Amount,
                d.AppliedAt,
                Reversed = d.Reversals.Sum(r => (decimal?)r.Amount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var existing = await context.RebateCreditAllocations
            .Where(a => a.Disbursement.Rebate.BookingId == bookingId)
            .ToListAsync(cancellationToken);

        if (installments.Count == 0)
        {
            // No schedule to land on. Whatever was allocated to a plan that has since been
            // regenerated away must go, or it would keep crediting installments that no longer exist.
            if (existing.Count != 0) context.RebateCreditAllocations.RemoveRange(existing);
            return;
        }

        var paidByInstallment = await context.Payments
            .Where(p => p.BookingId == bookingId && p.InstallmentId != null
                && p.Type == PaymentType.Installment)
            .GroupBy(p => p.InstallmentId!.Value)
            .Select(g => new { InstallmentId = g.Key, Amount = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.InstallmentId, x => x.Amount, cancellationToken);

        // What each installment still needs, before any booking-level credit is placed on it.
        var capacity = installments.ToDictionary(i => i.Id, i => Money(i.Amount
            - (paidByInstallment.TryGetValue(i.Id, out var paid) ? paid : 0m)
            - credits.Where(c => c.InstallmentId == i.Id).Sum(c => c.Amount - c.Reversed)));

        // Latest first: a concession comes off the tail of the plan, not off the payments the
        // customer is about to make. Ordering is fully deterministic so the same inputs always
        // produce the same allocation, whatever order the rows come back in.
        var targets = installments
            .OrderByDescending(i => i.DueDate)
            .ThenByDescending(i => i.SequenceNumber)
            .ThenByDescending(i => i.Id)
            .ToList();

        var bookingLevel = credits.Where(c => c.InstallmentId == null).ToList();
        var bookingLevelNet = Money(bookingLevel.Sum(c => c.Amount - c.Reversed));

        // How much of the booking-level credit the plan has NOT already been shrunk by. The plan
        // still demands (schedule total − cash collected on it − credits aimed at it); the customer
        // still owes (net sale − cash collected anywhere − every credit). Subtract the one from the
        // other and everything they share cancels, leaving this. Zero when the schedule was built
        // after the rebate — RemainingInstallmentPool already took it out — and the full credit when
        // the rebate landed on a plan that was already fixed.
        var placeable = Money(Math.Clamp(
            installments.Sum(i => i.Amount) + booking.BookingAmountReceived + bookingLevelNet
                - (booking.AgreedSalePrice - booking.DiscountAmount),
            0m,
            bookingLevelNet));

        var wanted = new List<(int DisbursementId, int InstallmentId, decimal Amount)>();
        // Newest credit first: when only part is placeable, the unabsorbed part is the one that
        // arrived after the plan was fixed.
        foreach (var credit in bookingLevel.OrderByDescending(c => c.AppliedAt).ThenByDescending(c => c.Id))
        {
            if (placeable <= 0m) break;
            var remaining = Money(Math.Min(credit.Amount - credit.Reversed, placeable));
            placeable = Money(placeable - remaining);
            foreach (var installment in targets)
            {
                if (remaining <= 0m) break;
                var room = capacity[installment.Id];
                if (room <= 0m) continue;
                var take = Math.Min(room, remaining);
                wanted.Add((credit.Id, installment.Id, take));
                capacity[installment.Id] = Money(room - take);
                remaining = Money(remaining - take);
            }
            // A leftover means the credit is larger than the schedule can absorb — it still reduces
            // the booking balance, it simply has no installment left to sit on. Nothing to record.
        }

        foreach (var row in existing)
        {
            var match = wanted.FirstOrDefault(w => w.DisbursementId == row.DisbursementId
                && w.InstallmentId == row.InstallmentId);
            if (match.Amount == 0m) context.RebateCreditAllocations.Remove(row);
            else if (row.Amount != match.Amount) row.Amount = match.Amount;
        }
        foreach (var row in wanted.Where(w => !existing.Any(e => e.DisbursementId == w.DisbursementId
            && e.InstallmentId == w.InstallmentId)))
        {
            context.RebateCreditAllocations.Add(new RebateCreditAllocation
            {
                DisbursementId = row.DisbursementId, InstallmentId = row.InstallmentId, Amount = row.Amount
            });
        }

        // An installment is settled by cash, by a credit aimed at it, or by a share of a
        // booking-level credit — the three are interchangeable to the customer, so the status has to
        // read all three or a fully credited installment never closes.
        foreach (var installment in installments)
        {
            var covered = Money(
                (paidByInstallment.TryGetValue(installment.Id, out var paid) ? paid : 0m)
                + credits.Where(c => c.InstallmentId == installment.Id).Sum(c => c.Amount - c.Reversed)
                + wanted.Where(w => w.InstallmentId == installment.Id).Sum(w => w.Amount));
            var status = covered >= installment.Amount ? InstallmentStatus.Paid
                : covered > 0m ? InstallmentStatus.PartiallyPaid
                : InstallmentStatus.Pending;
            if (installment.Status == status) continue;
            installment.Status = status;
            installment.PaidAt = status == InstallmentStatus.Paid ? settledAt : null;
        }
    }

    /// <summary>
    /// What has settled one installment: cash, a credit aimed at it, and its share of any
    /// booking-level credit. The counterpart of <see cref="ReallocateAsync"/> for callers that need
    /// one installment's figure rather than the whole schedule.
    /// </summary>
    public static async Task<decimal> InstallmentCreditsAsync(
        AppDbContext context,
        int installmentId,
        CancellationToken cancellationToken = default)
    {
        var direct = await context.RebateDisbursements
            .Where(d => d.InstallmentId == installmentId)
            .SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m;
        var reversed = await context.RebateDisbursementReversals
            .Where(r => r.Disbursement.InstallmentId == installmentId)
            .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;
        var allocated = await context.RebateCreditAllocations
            .Where(a => a.InstallmentId == installmentId)
            .SumAsync(a => (decimal?)a.Amount, cancellationToken) ?? 0m;
        return Money(direct - reversed + allocated);
    }

    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
