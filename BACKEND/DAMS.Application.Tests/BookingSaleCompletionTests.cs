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
        var project = new Project { ProjectName = "Completion", Location = "Karachi", CreatedById = 1 };
        var unit = new Unit { Project = project, UnitNumber = "C-1", UnitType = "Apartment", Price = 100_000m, Status = UnitStatus.OnPaymentPlan };
        var customer = new Customer { FullName = "Buyer", Phone = "03001112222", Status = CustomerStatus.Active };
        var booking = new Booking
        {
            BookingReference = "BK-COMPLETE", Customer = customer, Unit = unit,
            Status = BookingStatus.PaymentPlanActive, Source = CustomerSource.Referral,
            AgreedSalePrice = 100_000m, DiscountAmount = 0m, BookingAmountRequired = 20_000m,
            BookingAmountReceived = 20_000m, BookingDate = DateTime.UtcNow
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

        var service = new BookingService(db, new CustomerService(db), new FinanceAccountService(db));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteSaleAsync(booking.Id, 5));
        Assert.Contains("installments remain unpaid", error.Message);
        Assert.Equal(BookingStatus.PaymentPlanActive, db.Bookings.Single().Status);
        Assert.Equal(UnitStatus.OnPaymentPlan, db.Units.Single().Status);
    }

    private static AppDbContext Context()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        return db;
    }
}
