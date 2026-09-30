using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.DTOs.CustomerDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class BookingCreateTests
{
    private sealed class Harness
    {
        public AppDbContext Context { get; }
        public BookingService Service { get; }
        public CustomerService Customers { get; }
        public int UnitId { get; private set; }
        public string UnitNumber { get; private set; } = "";
        public int AccountId { get; private set; }

        private Harness(AppDbContext context)
        {
            Context = context;
            Customers = new CustomerService(context);
            Service = new BookingService(context, Customers, new FinanceAccountService(context), notifications: null, commissionLifecycle: null);
        }

        public static async Task<Harness> Create()
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
            context.Database.EnsureCreated();
            var harness = new Harness(context);

            var project = new Project { ProjectName = "Create Test", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit { Project = project, UnitNumber = "810", UnitType = "1 Bed", Price = 14_000_000m, Status = UnitStatus.Available };
            var account = new FinanceAccount { Name = "Cash Account", Type = FinanceAccountType.Cash, AccountHolderName = "Company", IsActive = true };
            context.AddRange(project, unit, account);
            await context.SaveChangesAsync();
            harness.UnitId = unit.Id;
            harness.UnitNumber = unit.UnitNumber;
            harness.AccountId = account.Id;
            return harness;
        }

        public CreateBookingDto Dto(Action<CreateBookingDto>? change = null)
        {
            var dto = new CreateBookingDto
            {
                UnitId = UnitId, Source = CustomerSource.WalkIn,
                NewCustomer = new NewCustomerForBookingDto { FullName = "Hamza Iqbal", Phone = "0300 1234567" },
                AgreedSalePrice = 14_000_000m, BookingAmountRequired = 1_400_000m
            };
            change?.Invoke(dto);
            return dto;
        }
    }

    [Fact]
    public async Task NewCustomer_IsCreatedWithTheBooking_AndNothingIsPaidYet()
    {
        var h = await Harness.Create();
        var result = await h.Service.CreateBookingAsync(h.Dto(), 1);

        Assert.Equal(BookingStatus.AwaitingBookingAmount, result.Status);
        Assert.Null(result.RecordedPaymentId);
        Assert.Equal("Hamza Iqbal", (await h.Context.Customers.SingleAsync()).FullName);
        Assert.False(await h.Context.Payments.AnyAsync());
    }

    [Fact]
    public async Task AFailedBooking_LeavesNoNewCustomerBehind()
    {
        var h = await Harness.Create();
        // Fails a booking rule (more than the price), which used to run after the customer was saved.
        var dto = h.Dto(d => d.BookingAmountRequired = 99_000_000m);

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.CreateBookingAsync(dto, 1));

        Assert.False(await h.Context.Customers.AnyAsync());
        Assert.False(await h.Context.Bookings.AnyAsync());
    }

    [Fact]
    public async Task AMissingAccount_LeavesNoNewCustomerBehind()
    {
        var h = await Harness.Create();
        var dto = h.Dto(d => { d.ApplicationAmountReceived = 100_000m; d.ApplicationPaymentMethod = PaymentMethod.Cash; });

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.CreateBookingAsync(dto, 1));

        Assert.False(await h.Context.Customers.AnyAsync());
    }

    [Fact]
    public async Task ATakenUnit_IsAConflictWithAClearMessage()
    {
        var h = await Harness.Create();
        await h.Service.CreateBookingAsync(h.Dto(), 1);

        var error = await Assert.ThrowsAsync<BookingConflictException>(() => h.Service.CreateBookingAsync(
            h.Dto(d => d.NewCustomer = new NewCustomerForBookingDto { FullName = "Someone Else", Phone = "0311 7654321" }), 1));

        Assert.Equal("Unit 810 was just booked by someone else. Pick another unit.", error.Message);
        // The loser leaves nothing behind.
        Assert.Single(await h.Context.Customers.ToListAsync());
    }

    [Fact]
    public async Task MoneyReceivedWithTheForm_KeepsItsRealMethodAndReference_AndTheLabelIsNotTheMethod()
    {
        var h = await Harness.Create();
        var result = await h.Service.CreateBookingAsync(h.Dto(d =>
        {
            d.ApplicationAmountReceived = 1_400_000m;
            d.ApplicationFinanceAccountId = h.AccountId;
            d.ApplicationPaymentMethod = PaymentMethod.BankTransfer;
            // The label printed on the form; it used to be read as the method.
            d.ApplicationPaymentType = "Booking";
            d.PaymentThrough = "TRX-90021";
            d.ApplicationDate = PakistanTime.Today;
        }), 1);

        var payment = await h.Context.Payments.SingleAsync();
        Assert.Equal(PaymentMethod.BankTransfer, payment.PaymentMethod);
        Assert.Equal("TRX-90021", payment.PaymentReference);
        Assert.Equal(payment.Id, result.RecordedPaymentId);
        Assert.Equal("Booking", (await h.Context.Bookings.SingleAsync()).ApplicationPaymentType);
        Assert.Equal(BookingStatus.PaymentPlanActive, result.Status);
    }

    [Fact]
    public async Task MoneyReceived_NeedsAPaymentMethod()
    {
        var h = await Harness.Create();
        var dto = h.Dto(d => { d.ApplicationAmountReceived = 100_000m; d.ApplicationFinanceAccountId = h.AccountId; d.ApplicationPaymentType = "Booking"; });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.CreateBookingAsync(dto, 1));

        Assert.Contains("Payment method is required", error.Message);
        Assert.False(await h.Context.Customers.AnyAsync());
    }

    [Theory]
    [InlineData("0300-1234567")]
    [InlineData("0300 1234567")]
    [InlineData("03001234567")]
    [InlineData("+92 300 1234567")]
    [InlineData("hamza")]
    public async Task CustomerSearch_FindsTheCustomerInAnyPhoneFormat_OrByName(string search)
    {
        var h = await Harness.Create();
        await h.Service.CreateBookingAsync(h.Dto(d => d.NewCustomer!.CNIC = "35202-1234567-1"), 1);

        var result = await h.Customers.GetCustomersAsync(new CustomerFilterDto { SearchTerm = search, Page = 1, PageSize = 20 });

        Assert.Equal("Hamza Iqbal", Assert.Single(result.Items).FullName);
    }

    [Theory]
    [InlineData("35202-1234567-1")]
    [InlineData("352021234567")]
    public async Task CustomerSearch_FindsTheCustomerByCnic_WithOrWithoutDashes(string search)
    {
        var h = await Harness.Create();
        await h.Service.CreateBookingAsync(h.Dto(d => d.NewCustomer!.CNIC = "35202-1234567-1"), 1);

        var result = await h.Customers.GetCustomersAsync(new CustomerFilterDto { SearchTerm = search, Page = 1, PageSize = 20 });

        Assert.Single(result.Items);
    }
}
