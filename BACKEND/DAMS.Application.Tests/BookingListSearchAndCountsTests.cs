using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>The bookings list: phone and CNIC search in any format, and the per-status counts behind the summary cards.</summary>
public sealed class BookingListSearchAndCountsTests
{
    [Theory]
    [InlineData("0300-1234567")]
    [InlineData("0300 1234567")]
    [InlineData("03001234567")]
    [InlineData("+92 300 1234567")]
    [InlineData("+923001234567")]
    [InlineData("0092 300 1234567")]
    public async Task Search_FindsTheSameCustomerByAnyPhoneFormat(string typed)
    {
        await using var h = await Harness.Create();
        var found = await h.Service.GetBookingsAsync(new BookingFilterDto { SearchTerm = typed });
        Assert.Equal(["BK-000001"], found.Items.Select(b => b.BookingReference));
    }

    [Fact]
    public async Task Search_FindsACustomerWhosePhoneWasStoredWithDashesOrACountryCode()
    {
        await using var h = await Harness.Create();
        Assert.Equal(["BK-000002"], (await h.Service.GetBookingsAsync(new BookingFilterDto { SearchTerm = "03334412987" })).Items.Select(b => b.BookingReference));
        Assert.Equal(["BK-000003"], (await h.Service.GetBookingsAsync(new BookingFilterDto { SearchTerm = "0345 6789012" })).Items.Select(b => b.BookingReference));
    }

    [Theory]
    [InlineData("35202-1234567-1")]
    [InlineData("3520212345671")]
    [InlineData("35202 1234567 1")]
    public async Task Search_FindsACustomerByCnicWithOrWithoutDashes(string typed)
    {
        await using var h = await Harness.Create();
        var found = await h.Service.GetBookingsAsync(new BookingFilterDto { SearchTerm = typed });
        Assert.Equal(["BK-000001"], found.Items.Select(b => b.BookingReference));
    }

    [Fact]
    public async Task Search_StillFindsByReferenceNameUnitAndProject()
    {
        await using var h = await Harness.Create();
        Assert.Single((await h.Service.GetBookingsAsync(new BookingFilterDto { SearchTerm = "bk-000002" })).Items);
        Assert.Single((await h.Service.GetBookingsAsync(new BookingFilterDto { SearchTerm = "hamza" })).Items);
        Assert.Single((await h.Service.GetBookingsAsync(new BookingFilterDto { SearchTerm = "B03" })).Items);
        Assert.Equal(3, (await h.Service.GetBookingsAsync(new BookingFilterDto { SearchTerm = "floria" })).TotalCount);
    }

    [Fact]
    public async Task Search_ForAShortNumberDoesNotMatchEveryPhoneContainingIt()
    {
        await using var h = await Harness.Create();
        // "000001" is part of a booking reference; it must not turn into a phone search for "1".
        var found = await h.Service.GetBookingsAsync(new BookingFilterDto { SearchTerm = "000001" });
        Assert.Equal(["BK-000001"], found.Items.Select(b => b.BookingReference));
    }

    [Fact]
    public async Task StatusCounts_IncludeCancelledInTheTotal()
    {
        await using var h = await Harness.Create();
        var counts = await h.Service.GetBookingStatusCountsAsync(new BookingFilterDto());
        Assert.Equal(6, counts.Total);
        Assert.Equal(2, counts.AwaitingBookingAmount);
        Assert.Equal(2, counts.PaymentPlanActive);
        Assert.Equal(1, counts.PossessionGiven);
        Assert.Equal(0, counts.SaleCompleted);
        Assert.Equal(1, counts.Cancelled);
    }

    [Fact]
    public async Task StatusCounts_FollowSearchAndProjectButNotStatus()
    {
        await using var h = await Harness.Create();
        var project = await h.Service.GetBookingStatusCountsAsync(new BookingFilterDto { ProjectId = h.DeenSquareId });
        Assert.Equal(3, project.Total);
        Assert.Equal(1, project.PaymentPlanActive);
        Assert.Equal(1, project.PossessionGiven);
        Assert.Equal(1, project.Cancelled);

        var searched = await h.Service.GetBookingStatusCountsAsync(new BookingFilterDto { SearchTerm = "0300-1234567" });
        Assert.Equal(1, searched.Total);
        Assert.Equal(1, searched.AwaitingBookingAmount);

        // A status on the filter is ignored, so choosing a card leaves the other cards' numbers alone.
        var withStatus = await h.Service.GetBookingStatusCountsAsync(new BookingFilterDto { Status = BookingStatus.Cancelled });
        Assert.Equal(6, withStatus.Total);
        Assert.Equal(2, withStatus.PaymentPlanActive);
    }

    [Fact]
    public async Task StatusCounts_AreZeroWhenNothingMatches()
    {
        await using var h = await Harness.Create();
        var counts = await h.Service.GetBookingStatusCountsAsync(new BookingFilterDto { SearchTerm = "BK-0999" });
        Assert.Equal(0, counts.Total);
        Assert.Equal(0, counts.AwaitingBookingAmount);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public AppDbContext Context { get; }
        public BookingService Service { get; }
        public int DeenSquareId { get; private set; }

        private Harness(AppDbContext context)
        {
            Context = context;
            Service = new BookingService(context, new CustomerService(context), new FinanceAccountService(context),
                notifications: null, commissionLifecycle: null);
        }

        public static async Task<Harness> Create()
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            context.Database.EnsureCreated();
            var harness = new Harness(context);
            var floria = new Project { ProjectName = "Floria Heights", Location = "Karachi", CreatedById = 1 };
            var deen = new Project { ProjectName = "Deen Square", Location = "Karachi", CreatedById = 1 };
            context.AddRange(floria, deen);

            void Add(int n, Project project, string unit, string name, string phone, string? cnic, BookingStatus status)
            {
                var customer = new Customer { FullName = name, Phone = phone, CNIC = cnic, Status = CustomerStatus.Active };
                var u = new Unit { Project = project, UnitNumber = unit, UnitType = "Apartment", Price = 1_000_000m, Status = UnitStatus.OnPaymentPlan };
                context.Bookings.Add(new Booking
                {
                    BookingReference = $"BK-{n:D6}", Customer = customer, Unit = u, Status = status,
                    Source = CustomerSource.Referral, AgreedSalePrice = 1_000_000m, BookingAmountRequired = 500_000m,
                    BookingDate = new DateTime(2026, 9, n, 0, 0, 0, DateTimeKind.Utc)
                });
            }

            Add(1, floria, "B10", "Kashif Butt", "03001234567", "35202-1234567-1", BookingStatus.AwaitingBookingAmount);
            Add(2, floria, "B09", "Hamza Iqbal", "0333-4412987", null, BookingStatus.AwaitingBookingAmount);
            Add(3, floria, "B03", "Bilal Ahmed", "+92 345 6789012", "42101-7654321-9", BookingStatus.PaymentPlanActive);
            Add(4, deen, "S-13", "Shazia Riaz", "03211413739", null, BookingStatus.PaymentPlanActive);
            Add(5, deen, "S-14", "Sana Malik", "03281551652", null, BookingStatus.PossessionGiven);
            Add(6, deen, "S-15", "Adeel Raza", "03351689565", null, BookingStatus.Cancelled);
            await context.SaveChangesAsync();
            harness.DeenSquareId = deen.Id;
            return harness;
        }

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
