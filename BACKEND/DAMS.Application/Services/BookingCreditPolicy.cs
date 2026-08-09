using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services;

/// <summary>One source of truth for non-cash credits that reduce a booking receivable.</summary>
internal static class BookingCreditPolicy
{
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

    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
