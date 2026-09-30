using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>KAN-78: the data printed on the receipt and on the application form.</summary>
public sealed class ReceiptAndApplicationFormDataTests
{
    [Fact]
    public async Task TheReceiptTower_IsTheBookingsTower_NotTheUnitNumberPrefix()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        var paymentId = await h.RecordPaymentAsync();
        var booking = await h.Db.Bookings.SingleAsync(b => b.Id == h.BookingId);
        booking.Tower = "Tower 2";
        booking.IsCorner = true;
        (await h.Db.Units.SingleAsync(u => u.Id == booking.UnitId)).UnitNumber = "A-606a";
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var receipt = await h.Bookings.GetPaymentReceiptAsync(h.BookingId, paymentId);

        Assert.Equal("Tower 2", receipt.Tower);
        Assert.True(receipt.IsCorner);
        Assert.Equal("A-606a", receipt.UnitNumber);
        Assert.Equal(PaymentMethod.BankTransfer, receipt.PaymentMethod);
        Assert.Equal(booking.BookingReference, receipt.BookingReference);
        Assert.Equal("TT-HARNESS-1", receipt.PaymentReference);
    }

    [Fact]
    public async Task TheReceiptTower_IsEmptyWhenTheBookingHasNone()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        var paymentId = await h.RecordPaymentAsync();
        (await h.Db.Units.FirstAsync()).UnitNumber = "B-101";
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        Assert.Null((await h.Bookings.GetPaymentReceiptAsync(h.BookingId, paymentId)).Tower);
    }

    [Fact]
    public async Task TheApplicationPaymentMethod_IsTheMethodOfTheFirstBookingAmountPayment()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.RecordPaymentAsync();
        var booking = await h.Db.Bookings.SingleAsync(b => b.Id == h.BookingId);
        booking.ApplicationAmountReceived = 250_000m;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var detail = await h.Bookings.GetBookingByIdAsync(h.BookingId);

        Assert.Equal(PaymentMethod.BankTransfer, detail!.ApplicationPaymentMethod);
    }

    [Fact]
    public async Task TheApplicationPaymentMethod_IsEmptyWhenNothingCameWithTheForm()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        await h.RecordPaymentAsync();
        h.Db.ChangeTracker.Clear();

        Assert.Null((await h.Bookings.GetBookingByIdAsync(h.BookingId))!.ApplicationPaymentMethod);
    }
}
