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
    /// What the customer still owes that the installment schedule does NOT demand — the principal a
    /// generated plan absorbed and that no later payment can reach.
    /// <para>
    /// The schedule is the only route DAMS has for collecting an installment balance, and a receipt
    /// is capped by the installment it is recorded against. So whenever the plan totals less than
    /// (net price − booking amount received − credits), the difference is debt with no collection
    /// path: it blocks completion for ever while being invisible on the schedule.
    /// </para>
    /// <para>
    /// The possession row is inside <paramref name="scheduleTotal"/>, so this deliberately does not
    /// subtract <c>booking.PossessionAmount</c> as well — reading the plan's own total is what keeps
    /// this from drifting away from the rows it is measuring.
    /// </para>
    /// </summary>
    public static decimal UncollectableShortfall(Booking booking, decimal scheduleTotal, decimal nonCashCredits) =>
        Money(Math.Max(0m, booking.AgreedSalePrice - booking.DiscountAmount
            - booking.BookingAmountReceived - nonCashCredits - scheduleTotal));

    /// <summary>
    /// Whether this booking's installment plan can still be replaced. One implementation, because
    /// two callers ask it for opposite reasons: the schedule service before it deletes and rebuilds
    /// the rows, and the rebate service before it reverses a credit the existing plan was built
    /// smaller by — a reversal that regenerating cannot repair is a reversal that strands the debt
    /// (see <see cref="UncollectableShortfall"/>).
    /// <para>A plan is pinned by anything the rebuild cannot legally take with it:</para>
    /// <list type="bullet">
    /// <item>an installment that is no longer Pending for a reason other than a booking-level credit
    /// allocated onto it — those allocations are derived bookkeeping, discarded and rebuilt against
    /// the new schedule;</item>
    /// <item>a recorded installment payment — the receipt names a row that is about to be deleted;</item>
    /// <item>ANY disbursement aimed at one of these installments, live or fully reversed. The
    /// disbursement keeps its <c>InstallmentId</c> for ever (a reversal is a new row, never an edit
    /// of the original) and that foreign key is <c>Restrict</c>, so deleting the installment fails in
    /// the database. Testing only for a live balance let validation pass and the save blow up.</item>
    /// </list>
    /// </summary>
    public static async Task<bool> CanRegenerateScheduleAsync(
        AppDbContext context,
        int bookingId,
        CancellationToken cancellationToken = default)
    {
        var installments = await context.Installments.AsNoTracking()
            .Where(i => i.BookingId == bookingId)
            .Select(i => new { i.Id, i.Status })
            .ToListAsync(cancellationToken);
        if (installments.Count == 0) return false;

        var creditedByAllocation = await context.RebateCreditAllocations.AsNoTracking()
            .Where(a => a.Disbursement.Rebate.BookingId == bookingId)
            .Select(a => a.InstallmentId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (installments.Any(i => i.Status != InstallmentStatus.Pending && !creditedByAllocation.Contains(i.Id)))
            return false;

        var hasInstallmentPayments = await context.Payments.AsNoTracking()
            .AnyAsync(p => p.BookingId == bookingId && p.InstallmentId != null
                && p.Type == PaymentType.Installment, cancellationToken);
        if (hasInstallmentPayments) return false;

        return !await context.RebateDisbursements.AsNoTracking()
            .AnyAsync(d => d.Rebate.BookingId == bookingId && d.InstallmentId != null, cancellationToken);
    }

    /// <summary>
    /// Net credit sitting on each installment of the given bookings — a credit aimed straight at it,
    /// less what has been reversed out, plus its share of any booking-level credit — keyed by
    /// installment id. The set-based counterpart of <see cref="InstallmentCreditsAsync"/>, so a
    /// booking projection and the schedule screen cannot report the same installment differently.
    /// </summary>
    public static async Task<Dictionary<int, decimal>> InstallmentCreditsByBookingAsync(
        AppDbContext context,
        IReadOnlyCollection<int> bookingIds,
        CancellationToken cancellationToken = default)
    {
        var totals = new Dictionary<int, decimal>();
        if (bookingIds.Count == 0) return totals;
        var ids = bookingIds.Distinct().ToList();

        var direct = await context.RebateDisbursements.AsNoTracking()
            .Where(d => ids.Contains(d.Rebate.BookingId) && d.InstallmentId != null)
            .GroupBy(d => d.InstallmentId!.Value)
            .Select(g => new { InstallmentId = g.Key, Amount = g.Sum(d => d.Amount) })
            .ToListAsync(cancellationToken);
        var reversed = await context.RebateDisbursementReversals.AsNoTracking()
            .Where(r => ids.Contains(r.Disbursement.Rebate.BookingId) && r.Disbursement.InstallmentId != null)
            .GroupBy(r => r.Disbursement.InstallmentId!.Value)
            .Select(g => new { InstallmentId = g.Key, Amount = g.Sum(r => r.Amount) })
            .ToListAsync(cancellationToken);
        var allocated = await context.RebateCreditAllocations.AsNoTracking()
            .Where(a => ids.Contains(a.Disbursement.Rebate.BookingId))
            .GroupBy(a => a.InstallmentId)
            .Select(g => new { InstallmentId = g.Key, Amount = g.Sum(a => a.Amount) })
            .ToListAsync(cancellationToken);

        foreach (var row in direct)
            totals[row.InstallmentId] = totals.GetValueOrDefault(row.InstallmentId) + row.Amount;
        foreach (var row in reversed)
            totals[row.InstallmentId] = totals.GetValueOrDefault(row.InstallmentId) - row.Amount;
        foreach (var row in allocated)
            totals[row.InstallmentId] = totals.GetValueOrDefault(row.InstallmentId) + row.Amount;
        return totals;
    }

    /// <summary>
    /// Net non-cash credit on each of the given bookings, keyed by booking id. The set-based
    /// counterpart of <see cref="GetNonCashCreditsAsync"/> for the list and projection screens, which
    /// must not issue one query per row.
    /// </summary>
    public static async Task<Dictionary<int, decimal>> NonCashCreditsByBookingAsync(
        AppDbContext context,
        IReadOnlyCollection<int> bookingIds,
        CancellationToken cancellationToken = default)
    {
        var totals = new Dictionary<int, decimal>();
        if (bookingIds.Count == 0) return totals;
        var ids = bookingIds.Distinct().ToList();

        var applied = await context.RebateDisbursements.AsNoTracking()
            .Where(d => ids.Contains(d.Rebate.BookingId)
                && (d.Method == CustomerRebateMethod.OutstandingBalanceReduction
                    || d.Method == CustomerRebateMethod.InstallmentAdjustment
                    || d.Method == CustomerRebateMethod.CreditNote))
            .GroupBy(d => d.Rebate.BookingId)
            .Select(g => new { BookingId = g.Key, Amount = g.Sum(d => d.Amount) })
            .ToListAsync(cancellationToken);
        var reversed = await context.RebateDisbursementReversals.AsNoTracking()
            .Where(r => ids.Contains(r.Disbursement.Rebate.BookingId)
                && (r.Disbursement.Method == CustomerRebateMethod.OutstandingBalanceReduction
                    || r.Disbursement.Method == CustomerRebateMethod.InstallmentAdjustment
                    || r.Disbursement.Method == CustomerRebateMethod.CreditNote))
            .GroupBy(r => r.Disbursement.Rebate.BookingId)
            .Select(g => new { BookingId = g.Key, Amount = g.Sum(r => r.Amount) })
            .ToListAsync(cancellationToken);

        foreach (var row in applied)
            totals[row.BookingId] = totals.GetValueOrDefault(row.BookingId) + row.Amount;
        foreach (var row in reversed)
            totals[row.BookingId] = totals.GetValueOrDefault(row.BookingId) - row.Amount;
        foreach (var key in totals.Keys.ToList())
            totals[key] = Money(totals[key]);
        return totals;
    }

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
