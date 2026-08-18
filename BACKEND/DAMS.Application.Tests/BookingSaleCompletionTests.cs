using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class BookingSaleCompletionTests
{
    [Fact]
    public async Task CompleteSale_IsBlocked_WhileScheduledInstallmentsRemainUnpaid()
    {
        await using var db = Context();
        // Possession has already been given, so the sale is recognised and the only question left
        // is whether the schedule was settled. Completion is a paperwork milestone at this point.
        var booking = await SeedAsync(db, BookingStatus.PossessionGiven, recognised: true);

        var service = new BookingService(db, new CustomerService(db), new FinanceAccountService(db));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteSaleAsync(booking.Id, 5));
        Assert.Contains("installments remain unpaid", error.Message);
        Assert.Equal(BookingStatus.PossessionGiven, db.Bookings.Single().Status);
        Assert.Equal(UnitStatus.OnPaymentPlan, db.Units.Single().Status);
    }

    /// <summary>
    /// The legacy shortcut, closed. Completing straight from an active payment plan would finish a
    /// sale that had never been recognised as revenue — no possession event, so no recognition
    /// date, so nothing for a historical report to hang the income on.
    /// </summary>
    [Fact]
    public async Task CompleteSale_CannotBypassPossession()
    {
        await using var db = Context();
        var booking = await SeedAsync(db, BookingStatus.PaymentPlanActive, recognised: false);

        var service = new BookingService(db, new CustomerService(db), new FinanceAccountService(db));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteSaleAsync(booking.Id, 5));
        Assert.Contains("Give possession before completing the sale", error.Message);
        Assert.Equal(BookingStatus.PaymentPlanActive, db.Bookings.Single().Status);
        Assert.Empty(db.BookingSaleRecognitions);
    }

    private static async Task<Booking> SeedAsync(AppDbContext db, BookingStatus status, bool recognised)
    {
        var project = new Project { ProjectName = "Completion", Location = "Karachi", CreatedById = 1 };
        var unit = new Unit { Project = project, UnitNumber = "C-1", UnitType = "Apartment", Price = 100_000m, Status = UnitStatus.OnPaymentPlan };
        var customer = new Customer { FullName = "Buyer", Phone = "03001112222", Status = CustomerStatus.Active };
        var booking = new Booking
        {
            BookingReference = "BK-COMPLETE", Customer = customer, Unit = unit,
            Status = status, Source = CustomerSource.Referral,
            AgreedSalePrice = 100_000m, DiscountAmount = 0m, BookingAmountRequired = 20_000m,
            BookingAmountReceived = 20_000m, BookingDate = DateTime.UtcNow,
            PossessionDate = recognised ? DateTime.UtcNow : null
        };
        // Cash fully covers the sale price, so nothing is outstanding by balance...
        booking.Payments.Add(new Payment { Booking = booking, Amount = 100_000m, Type = PaymentType.BookingAmount, PaymentMethod = PaymentMethod.Cash });
        // ...but a scheduled installment is still unpaid, so completion must be refused.
        booking.Installments.Add(new Installment
        {
            Booking = booking, SequenceNumber = 1, Amount = 80_000m, Status = InstallmentStatus.Pending,
            DueDate = DateTime.UtcNow.AddDays(30), Type = InstallmentType.Regular
        });
        db.AddRange(project, unit, customer, booking);
        await db.SaveChangesAsync();
        if (recognised)
        {
            db.BookingSaleRecognitions.Add(new BookingSaleRecognition
            {
                BookingId = booking.Id, RecognitionDate = DAMS.Application.Common.PakistanTime.Today,
                NetSaleValue = 100_000m, RecognizedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        return booking;
    }

    private static AppDbContext Context()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        return db;
    }
}
