using DAMS.Application.DTOs.InstallmentDtos;
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

    /// <summary>
    /// A schedule may be built after possession — it is the only route DAMS has for collecting a
    /// recognised receivable — but it must not RESTATE the sale it collects.
    /// <para>
    /// BookingSaleRecognition.NetSaleValue is what was booked as revenue and what the Balance Sheet
    /// reports as Accounts Receivable, and it is immutable by design. Writing a new price onto the
    /// booking would leave the formal statements on the recognised figure while the dashboard's
    /// outstanding, the completion requirement and every commission basis moved to the new one: one
    /// sale, three different amounts, and no way to tell from any single screen which is right.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GeneratingAScheduleAfterPossession_CannotRepriceTheRecognisedSale()
    {
        await using var db = Context();
        var booking = await SeedAsync(db, BookingStatus.PossessionGiven, recognised: true);
        db.Installments.RemoveRange(db.Installments);
        await db.SaveChangesAsync();
        var service = new InstallmentService(db, new FinanceAccountService(db));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateScheduleAsync(booking.Id, new GenerateInstallmentPlanDto
            {
                AgreedSalePrice = 120_000m, DiscountPercent = 0m,
                Frequency = InstallmentFrequency.Monthly, NumberOfInstallments = 4,
                InstallmentStartDate = DateTime.UtcNow.AddDays(30)
            }, adminUserId: 5));

        Assert.Contains("recognised at possession", error.Message);
        Assert.Contains("cannot be repriced", error.Message);
        // The booking is untouched: no half-applied reprice, and no schedule built against one.
        var stored = await db.Bookings.AsNoTracking().SingleAsync();
        Assert.Equal(100_000m, stored.AgreedSalePrice);
        Assert.Equal(0m, stored.DiscountAmount);
        Assert.Empty(db.Installments);
        Assert.Equal(100_000m, db.BookingSaleRecognitions.Single().NetSaleValue);
    }

    /// <summary>
    /// The same call at the recognised price still works. Blocking the reprice must not strand the
    /// receivable with no way to collect it — that was the reason post-possession generation was
    /// allowed in the first place.
    /// </summary>
    [Fact]
    public async Task GeneratingAScheduleAfterPossession_AtTheRecognisedPrice_IsAllowed()
    {
        await using var db = Context();
        var booking = await SeedAsync(db, BookingStatus.PossessionGiven, recognised: true);
        db.Installments.RemoveRange(db.Installments);
        await db.SaveChangesAsync();
        var service = new InstallmentService(db, new FinanceAccountService(db));

        var schedule = await service.GenerateScheduleAsync(booking.Id, new GenerateInstallmentPlanDto
        {
            AgreedSalePrice = 100_000m, DiscountPercent = 0m,
            Frequency = InstallmentFrequency.Monthly, NumberOfInstallments = 4,
            InstallmentStartDate = DateTime.UtcNow.AddDays(30)
        }, adminUserId: 5);

        Assert.Equal(4, schedule.Items.Count);
        Assert.Equal(100_000m, (await db.Bookings.AsNoTracking().SingleAsync()).AgreedSalePrice);
    }

    /// <summary>
    /// A discount that arrives at the same net sale value is not a reprice. The recognised figure is
    /// the net one, so that is what the guard compares — not the gross price it was derived from.
    /// </summary>
    [Fact]
    public async Task GeneratingAScheduleAfterPossession_AllowsADifferentSplitAtTheSameNetValue()
    {
        await using var db = Context();
        var booking = await SeedAsync(db, BookingStatus.PossessionGiven, recognised: true);
        db.Installments.RemoveRange(db.Installments);
        await db.SaveChangesAsync();
        var service = new InstallmentService(db, new FinanceAccountService(db));

        // 125,000 less 20% is the same 100,000 the sale was recognised at.
        var schedule = await service.GenerateScheduleAsync(booking.Id, new GenerateInstallmentPlanDto
        {
            AgreedSalePrice = 125_000m, DiscountPercent = 20m,
            Frequency = InstallmentFrequency.Monthly, NumberOfInstallments = 4,
            InstallmentStartDate = DateTime.UtcNow.AddDays(30)
        }, adminUserId: 5);

        Assert.Equal(4, schedule.Items.Count);
        var stored = await db.Bookings.AsNoTracking().SingleAsync();
        Assert.Equal(100_000m, stored.AgreedSalePrice - stored.DiscountAmount);
    }

    /// <summary>Before possession there is no recognised sale, so the terms are still negotiable.</summary>
    [Fact]
    public async Task GeneratingAScheduleBeforePossession_CanStillSetTheTerms()
    {
        await using var db = Context();
        var booking = await SeedAsync(db, BookingStatus.PaymentPlanActive, recognised: false);
        db.Installments.RemoveRange(db.Installments);
        await db.SaveChangesAsync();
        var service = new InstallmentService(db, new FinanceAccountService(db));

        await service.GenerateScheduleAsync(booking.Id, new GenerateInstallmentPlanDto
        {
            AgreedSalePrice = 120_000m, DiscountPercent = 0m,
            Frequency = InstallmentFrequency.Monthly, NumberOfInstallments = 4,
            InstallmentStartDate = DateTime.UtcNow.AddDays(30)
        }, adminUserId: 5);

        Assert.Equal(120_000m, (await db.Bookings.AsNoTracking().SingleAsync()).AgreedSalePrice);
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
