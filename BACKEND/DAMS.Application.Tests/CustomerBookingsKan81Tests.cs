using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>KAN-81: the customer's Bookings tab — every booking, newest first, with the booking page's own figures.</summary>
public sealed class CustomerBookingsKan81Tests
{
    private sealed class World
    {
        public required AppDbContext Db { get; init; }
        public required BookingService Bookings { get; init; }
        public required Customer Customer { get; init; }
        public required Project Project { get; init; }
        private int _unit;

        public Booking Add(Customer customer, DateTime date, BookingStatus status = BookingStatus.PaymentPlanActive,
            decimal price = 1_000_000m, decimal discount = 0m, params decimal[] payments)
        {
            var unit = new Unit { Project = Project, UnitNumber = $"U-{++_unit}", UnitType = "Apartment", Price = price, Status = UnitStatus.OnPaymentPlan };
            var booking = new Booking
            {
                BookingReference = $"BK-{_unit:D6}", Customer = customer, Unit = unit, Status = status, Source = CustomerSource.Referral,
                ListPrice = price, AgreedSalePrice = price, DiscountAmount = discount, BookingAmountRequired = 100_000m,
                BookingAmountReceived = payments.Sum(), BookingDate = date
            };
            foreach (var amount in payments)
                booking.Payments.Add(new Payment { Booking = booking, Amount = amount, Type = PaymentType.BookingAmount, PaymentMethod = PaymentMethod.Cash, PaidAt = date });
            Db.Bookings.Add(booking);
            return booking;
        }

        public static async Task<World> CreateAsync()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Database.EnsureCreated();
            var project = new Project { ProjectName = "Floria Heights", Location = "Karachi", CreatedById = 1 };
            var customer = new Customer { FullName = "Usman Tariq", Phone = "03334412987", Status = CustomerStatus.Active };
            db.AddRange(project, customer);
            await db.SaveChangesAsync();
            return new World
            {
                Db = db, Project = project, Customer = customer,
                Bookings = new BookingService(db, new CustomerService(db), new FinanceAccountService(db))
            };
        }
    }

    [Fact]
    public async Task Lists_the_customers_bookings_newest_first_and_only_theirs()
    {
        var w = await World.CreateAsync();
        var other = new Customer { FullName = "Someone Else", Phone = "03001110000", Status = CustomerStatus.Active };
        w.Db.Customers.Add(other);
        var older = w.Add(w.Customer, new DateTime(2026, 4, 20));
        var newer = w.Add(w.Customer, new DateTime(2026, 7, 7));
        w.Add(other, new DateTime(2026, 8, 1));
        await w.Db.SaveChangesAsync();

        var rows = await w.Bookings.GetCustomerBookingsAsync(w.Customer.Id);

        Assert.Equal([newer.Id, older.Id], rows.Select(r => r.Id));
        Assert.Equal(("Floria Heights", "BK-000002"), (rows[0].ProjectName, rows[0].BookingReference));
    }

    [Fact]
    public async Task Nothing_is_silently_cut_off()
    {
        var w = await World.CreateAsync();
        for (var i = 0; i < 130; i++) w.Add(w.Customer, new DateTime(2026, 1, 1).AddDays(i));
        await w.Db.SaveChangesAsync();

        var rows = await w.Bookings.GetCustomerBookingsAsync(w.Customer.Id);

        Assert.Equal(130, rows.Count);
        // …and it is the number the customer page reports.
        Assert.Equal(130, (await new CustomerService(w.Db).GetCustomerByIdAsync(w.Customer.Id))!.BookingsCount);
    }

    [Fact]
    public async Task Net_price_paid_and_still_due_are_the_booking_pages_own_figures()
    {
        var w = await World.CreateAsync();
        var booking = w.Add(w.Customer, new DateTime(2026, 4, 20), price: 12_800_000m, discount: 800_000m, payments: [500_000m, 3_672_000m]);
        await w.Db.SaveChangesAsync();

        var row = Assert.Single(await w.Bookings.GetCustomerBookingsAsync(w.Customer.Id));
        var page = (await w.Bookings.GetBookingByIdAsync(booking.Id))!;

        Assert.Equal(12_000_000m, row.NetPrice);
        Assert.Equal(4_172_000m, row.Collected);
        Assert.Equal(7_828_000m, row.Outstanding);
        Assert.Equal((page.Collected, page.Outstanding), (row.Collected, row.Outstanding));
    }

    [Fact]
    public async Task A_cancelled_booking_owes_nothing_more()
    {
        var w = await World.CreateAsync();
        w.Add(w.Customer, new DateTime(2026, 4, 20), BookingStatus.Cancelled, payments: [200_000m]);
        await w.Db.SaveChangesAsync();

        var row = Assert.Single(await w.Bookings.GetCustomerBookingsAsync(w.Customer.Id));

        Assert.Equal(BookingStatus.Cancelled, row.Status);
        Assert.Equal(0m, row.Outstanding);
        Assert.Equal(200_000m, row.Collected);
    }
}
