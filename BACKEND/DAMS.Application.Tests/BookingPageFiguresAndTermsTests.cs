using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.DTOs.InstallmentDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>KAN-73: the figures the booking page reads, the terms history and rules, and the payment input rules.</summary>
public sealed class BookingPageFiguresAndTermsTests
{
    private static DateTime Today => PakistanTime.Today;

    // ── Figures on the booking detail ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Detail_CarriesCollectedOutstandingAndInstallmentCounts()
    {
        await using var h = await Harness.Create(BookingStatus.PaymentPlanActive);
        await h.AddPlan(paidCount: 2, unpaidCount: 3, unpaidStartsDaysFromToday: -4);

        var detail = (await h.Bookings.GetBookingByIdAsync(h.BookingId))!;

        Assert.Equal(700_000m, detail.Collected);
        Assert.Equal(300_000m, detail.Outstanding);
        Assert.Equal(2, detail.InstallmentsPaid);
        Assert.Equal(5, detail.InstallmentsTotal);
        Assert.True(detail.HasInstallmentSchedule);
    }

    [Fact]
    public async Task NextInstallment_IsTheEarliestUnpaidOne_WithWhatIsLeftOnIt()
    {
        await using var h = await Harness.Create(BookingStatus.PaymentPlanActive);
        await h.AddPlan(paidCount: 1, unpaidCount: 3, unpaidStartsDaysFromToday: -4);
        // Part of the first unpaid installment (100,000) has already been received.
        var first = h.Context.Installments.Where(i => i.Status != InstallmentStatus.Paid).OrderBy(i => i.DueDate).First();
        h.Context.Payments.Add(new Payment
        {
            BookingId = h.BookingId, InstallmentId = first.Id, Type = PaymentType.Installment,
            Amount = 40_000m, PaymentMethod = PaymentMethod.Cash, PaidAt = DateTime.UtcNow
        });
        await h.Context.SaveChangesAsync();

        var next = (await h.Bookings.GetBookingByIdAsync(h.BookingId))!.NextInstallment!;

        Assert.Equal(first.Id, next.Id);
        Assert.Equal(60_000m, next.Amount);
        Assert.True(next.IsOverdue);
        Assert.False(next.IsPossession);
    }

    [Fact]
    public async Task NextInstallment_IsNotOverdue_WhenItFallsDueLater()
    {
        await using var h = await Harness.Create(BookingStatus.PaymentPlanActive);
        await h.AddPlan(paidCount: 0, unpaidCount: 2, unpaidStartsDaysFromToday: 10);

        var next = (await h.Bookings.GetBookingByIdAsync(h.BookingId))!.NextInstallment!;

        Assert.False(next.IsOverdue);
        Assert.Equal(Today.AddDays(10), next.DueDate.Date);
    }

    [Fact]
    public async Task NoPlanYet_HasNoNextInstallmentAndZeroCounts()
    {
        await using var h = await Harness.Create(BookingStatus.PaymentPlanActive);

        var detail = (await h.Bookings.GetBookingByIdAsync(h.BookingId))!;

        Assert.Null(detail.NextInstallment);
        Assert.False(detail.HasInstallmentSchedule);
        Assert.Equal(0, detail.InstallmentsTotal);
        Assert.Equal(500_000m, detail.Collected);
        Assert.Equal(500_000m, detail.Outstanding);
    }

    [Fact]
    public async Task Outstanding_TakesRebateCreditsOff_AndIsZeroWhenCancelled()
    {
        await using var h = await Harness.Create(BookingStatus.PaymentPlanActive);
        var detail = (await h.Bookings.GetBookingByIdAsync(h.BookingId))!;
        Assert.Equal(detail.NetSalePrice - detail.Collected - detail.RebateCredits, detail.Outstanding);

        var booking = h.Context.Bookings.Single();
        booking.Status = BookingStatus.Cancelled;
        await h.Context.SaveChangesAsync();

        var cancelled = (await h.Bookings.GetBookingByIdAsync(h.BookingId))!;
        Assert.Equal(0m, cancelled.Outstanding);
        Assert.Null(cancelled.NextInstallment);
    }

    [Fact]
    public async Task ConvertedBooking_NamesItsLead_AndTheCustomersOwnCopyDoesNot()
    {
        await using var h = await Harness.Create(BookingStatus.AwaitingBookingAmount);
        Assert.Null((await h.Bookings.GetBookingByIdAsync(h.BookingId))!.ConvertedFromLead);

        h.Context.Leads.Add(new Lead { FirstName = "Hamza", LastName = "Iqbal", LeadReference = "LD-000437", ConvertedBookingId = h.BookingId });
        await h.Context.SaveChangesAsync();
        var leadId = h.Context.Leads.Single().Id;

        var lead = (await h.Bookings.GetBookingByIdAsync(h.BookingId))!.ConvertedFromLead!;
        Assert.Equal(leadId, lead.LeadId);
        Assert.Equal("LD-000437", lead.LeadReference);

        var own = await h.Bookings.GetBookingForUserAsync(h.BookingId, Harness.CustomerUserId);
        Assert.Null(own!.ConvertedFromLead);
    }

    // ── Set / Edit terms ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SavingTerms_WritesAHistoryRowWithOldAndNewValues()
    {
        await using var h = await Harness.Create(BookingStatus.AwaitingBookingAmount);
        var before = h.Context.Bookings.AsNoTracking().Single();

        await h.Bookings.UpdateBookingFinancialsAsync(h.BookingId, new UpdateBookingFinancialsDto
        {
            AgreedSalePrice = 900_000m, DiscountPercent = 10m, DiscountReason = "Walk-in offer",
            BookingAmountRequired = 200_000m, BookingAmountDueDate = Today.AddDays(7)
        }, adminUserId: 9);

        var row = Assert.Single(h.Context.BookingTermsHistories);
        Assert.Equal(BookingTermsChangeSource.Terms, row.Source);
        Assert.Equal(9, row.ChangedByUserId);
        Assert.Equal(before.AgreedSalePrice, row.OldAgreedSalePrice);
        Assert.Equal(900_000m, row.NewAgreedSalePrice);
        Assert.Equal(10m, row.NewDiscountPercent);
        Assert.Equal("Walk-in offer", row.NewDiscountReason);
        Assert.Equal(before.BookingAmountRequired, row.OldBookingAmountRequired);
        Assert.Equal(200_000m, row.NewBookingAmountRequired);
        Assert.Equal(Today.AddDays(7), row.NewBookingAmountDueDate);
    }

    [Fact]
    public async Task DiscountReason_IsKeptWhenNotSent_ReplacedWhenSent_ClearedWhenBlank()
    {
        await using var h = await Harness.Create(BookingStatus.AwaitingBookingAmount);
        UpdateBookingFinancialsDto Terms(string? reason) => new()
        {
            AgreedSalePrice = 1_000_000m, DiscountPercent = 5m, DiscountReason = reason, BookingAmountRequired = 500_000m
        };

        await h.Bookings.UpdateBookingFinancialsAsync(h.BookingId, Terms("Early payment"), 9);
        await h.Bookings.UpdateBookingFinancialsAsync(h.BookingId, Terms(null), 9);
        Assert.Equal("Early payment", h.Context.Bookings.AsNoTracking().Single().DiscountReason);

        await h.Bookings.UpdateBookingFinancialsAsync(h.BookingId, Terms("Staff referral"), 9);
        Assert.Equal("Staff referral", h.Context.Bookings.AsNoTracking().Single().DiscountReason);

        await h.Bookings.UpdateBookingFinancialsAsync(h.BookingId, Terms("  "), 9);
        Assert.Null(h.Context.Bookings.AsNoTracking().Single().DiscountReason);
    }

    [Fact]
    public async Task DueByDate_MustBeTodayOrLater()
    {
        await using var h = await Harness.Create(BookingStatus.AwaitingBookingAmount);
        UpdateBookingFinancialsDto Terms(DateTime? due) => new()
        {
            AgreedSalePrice = 1_000_000m, DiscountPercent = 0m, BookingAmountRequired = 500_000m, BookingAmountDueDate = due
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Bookings.UpdateBookingFinancialsAsync(h.BookingId, Terms(Today.AddDays(-1)), 9));
        Assert.Contains("today or later", error.Message);
        Assert.Empty(h.Context.BookingTermsHistories);

        await h.Bookings.UpdateBookingFinancialsAsync(h.BookingId, Terms(Today), 9);
        await h.Bookings.UpdateBookingFinancialsAsync(h.BookingId, Terms(null), 9);
        Assert.Equal(2, h.Context.BookingTermsHistories.Count());
    }

    [Fact]
    public async Task Terms_CanOnlyBeSetWhileAwaitingTheBookingAmount_AndLeaveNoHistoryOtherwise()
    {
        await using var h = await Harness.Create(BookingStatus.PaymentPlanActive);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Bookings.UpdateBookingFinancialsAsync(h.BookingId, new UpdateBookingFinancialsDto
            { AgreedSalePrice = 1_000_000m, BookingAmountRequired = 300_000m }, 9));

        Assert.Empty(h.Context.BookingTermsHistories);
    }

    [Fact]
    public async Task ChangingPriceOrDiscountThroughThePlan_WritesAHistoryRow_AndKeepsTheReason()
    {
        await using var h = await Harness.Create(BookingStatus.PaymentPlanActive);
        h.Context.Bookings.Single().DiscountReason = "Early payment";
        await h.Context.SaveChangesAsync();
        var plan = new InstallmentService(h.Context, new FinanceAccountService(h.Context));

        GenerateInstallmentPlanDto Dto(decimal discount, bool regenerate) => new()
        {
            AgreedSalePrice = 1_000_000m, DiscountPercent = discount, Frequency = InstallmentFrequency.Monthly,
            NumberOfInstallments = 4, InstallmentStartDate = Today.AddDays(30), Regenerate = regenerate
        };

        // Same price and discount: nothing changed, so nothing is recorded.
        await plan.GenerateScheduleAsync(h.BookingId, Dto(0m, regenerate: false), adminUserId: 9);
        Assert.Empty(h.Context.BookingTermsHistories);

        await plan.GenerateScheduleAsync(h.BookingId, Dto(2m, regenerate: true), adminUserId: 9);

        var row = Assert.Single(h.Context.BookingTermsHistories);
        Assert.Equal(BookingTermsChangeSource.Plan, row.Source);
        Assert.Equal(9, row.ChangedByUserId);
        Assert.Equal(0m, row.OldDiscountPercent);
        Assert.Equal(2m, row.NewDiscountPercent);
        Assert.Equal("Early payment", row.NewDiscountReason);
        Assert.Equal("Early payment", h.Context.Bookings.AsNoTracking().Single().DiscountReason);
    }

    // ── Payment input rules ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BookingAmountPayment_RequiresAMethod_AndAReferenceUnlessCash()
    {
        await using var h = await Harness.Create(BookingStatus.AwaitingBookingAmount);
        RecordBookingAmountPaymentDto Pay(PaymentMethod? method, string? reference) => new()
        {
            Amount = 10_000m, PaymentMethod = method, PaymentReference = reference, FinanceAccountId = h.AccountId
        };

        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Bookings.RecordBookingAmountPaymentAsync(h.BookingId, Pay(null, null), 9));
        Assert.Contains("method is required", missing.Message);

        foreach (var method in new[] { PaymentMethod.BankTransfer, PaymentMethod.Cheque, PaymentMethod.Online })
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                h.Bookings.RecordBookingAmountPaymentAsync(h.BookingId, Pay(method, "  "), 9));
            Assert.Contains("reference is required", error.Message);
        }
        Assert.Single(h.Context.Payments);

        await h.Bookings.RecordBookingAmountPaymentAsync(h.BookingId, Pay(PaymentMethod.Cash, null), 9);
        await h.Bookings.RecordBookingAmountPaymentAsync(h.BookingId, Pay(PaymentMethod.Cheque, " CHQ-1001 "), 9);

        Assert.Equal(new[] { null, null, "CHQ-1001" }, h.Context.Payments.OrderBy(p => p.Id).Select(p => p.PaymentReference).ToArray());
    }

    [Fact]
    public async Task InstallmentPayment_RequiresAMethod_AndAReferenceUnlessCash()
    {
        await using var h = await Harness.Create(BookingStatus.PaymentPlanActive);
        await h.AddPlan(paidCount: 0, unpaidCount: 2, unpaidStartsDaysFromToday: 5);
        var installmentId = h.Context.Installments.OrderBy(i => i.DueDate).First().Id;
        var service = new InstallmentService(h.Context, new FinanceAccountService(h.Context));
        RecordInstallmentPaymentDto Pay(PaymentMethod? method, string? reference) => new()
        {
            Amount = 10_000m, PaymentMethod = method, PaymentReference = reference, FinanceAccountId = h.AccountId
        };
        var before = h.Context.Payments.Count();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordInstallmentPaymentAsync(h.BookingId, installmentId, Pay(null, "X"), 9));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordInstallmentPaymentAsync(h.BookingId, installmentId, Pay(PaymentMethod.Online, null), 9));
        Assert.Contains("reference is required", error.Message);
        Assert.Equal(before, h.Context.Payments.Count());

        await service.RecordInstallmentPaymentAsync(h.BookingId, installmentId, Pay(PaymentMethod.BankTransfer, "TRX-9"), 9);
        Assert.Equal(before + 1, h.Context.Payments.Count());
    }

    // ── Two people at once ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LosingARaceOnTerms_Possession_OrComplete_GivesThePlainMessage_NotAServerError()
    {
        foreach (var (status, act) in new (BookingStatus, Func<BookingService, int, Task>)[]
        {
            (BookingStatus.AwaitingBookingAmount, (s, id) => s.UpdateBookingFinancialsAsync(id, new UpdateBookingFinancialsDto
                { AgreedSalePrice = 1_000_000m, BookingAmountRequired = 300_000m }, 9)),
            (BookingStatus.PaymentPlanActive, (s, id) => s.GivePossessionAsync(id, null, 9)),
            (BookingStatus.PossessionGiven, (s, id) => s.CompleteSaleAsync(id, 9)),
        })
        {
            await using var h = await Harness.Create(status);
            h.ArmConflict();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => act(h.Bookings, h.BookingId));

            Assert.Equal("This booking was just changed by someone else. Refresh and try again.", error.Message);
        }
    }

    // ── Harness ───────────────────────────────────────────────────────────────────────────────

    private sealed class ConflictingContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public bool ConflictOnSave { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            ConflictOnSave ? throw new DbUpdateConcurrencyException("row version changed") : base.SaveChangesAsync(cancellationToken);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public const int CustomerUserId = 7;
        private readonly ConflictingContext _context;
        public AppDbContext Context => _context;
        public BookingService Bookings { get; }
        public int BookingId { get; private set; }
        public int AccountId { get; private set; }

        private Harness(ConflictingContext context)
        {
            _context = context;
            Bookings = new BookingService(context, new CustomerService(context), new FinanceAccountService(context));
        }

        public void ArmConflict() => _context.ConflictOnSave = true;

        public static async Task<Harness> Create(BookingStatus status)
        {
            var context = new ConflictingContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            context.Database.EnsureCreated();
            var h = new Harness(context);

            var project = new Project { ProjectName = "Floria Heights", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit { Project = project, UnitNumber = "B08", UnitType = "2 Bed", Price = 1_000_000m, Status = UnitStatus.OnPaymentPlan };
            var customer = new Customer { FullName = "Usman Tariq", Phone = "03334412987", Status = CustomerStatus.Active, UserId = CustomerUserId };
            var awaiting = status == BookingStatus.AwaitingBookingAmount;
            // A possession-given sale that is fully paid, so completing it is only ever blocked by a race.
            var paidInFull = status == BookingStatus.PossessionGiven;
            var booking = new Booking
            {
                BookingReference = $"BK-{Guid.NewGuid():N}", Customer = customer, Unit = unit, Status = status,
                Source = CustomerSource.Referral, ListPrice = 1_000_000m, AgreedSalePrice = 1_000_000m, DiscountAmount = 0m,
                BookingAmountRequired = 500_000m, BookingAmountReceived = awaiting ? 100_000m : paidInFull ? 1_000_000m : 500_000m,
                BookingDate = DateTime.UtcNow.AddDays(-20)
            };
            booking.Payments.Add(new Payment
            {
                Booking = booking, Amount = booking.BookingAmountReceived, Type = PaymentType.BookingAmount,
                PaymentMethod = PaymentMethod.Cash, PaidAt = DateTime.UtcNow.AddDays(-15)
            });
            var account = new FinanceAccount { Name = "Cash in hand", Type = FinanceAccountType.Cash, AccountHolderName = "Head office", IsActive = true };
            context.AddRange(project, unit, customer, booking, account);
            if (paidInFull)
                context.BookingSaleRecognitions.Add(new BookingSaleRecognition
                { Booking = booking, RecognitionDate = Today, NetSaleValue = 1_000_000m });
            await context.SaveChangesAsync();
            h.BookingId = booking.Id;
            h.AccountId = account.Id;
            return h;
        }

        /// <summary>
        /// Adds installments of 100,000 each: paid ones first (each with its cash payment), then unpaid
        /// ones falling due from today + the given offset, a month apart.
        /// </summary>
        public async Task AddPlan(int paidCount, int unpaidCount, int unpaidStartsDaysFromToday)
        {
            var booking = Context.Bookings.Single();
            var number = 1;
            for (var i = 0; i < paidCount; i++, number++)
            {
                var paid = new Installment
                {
                    BookingId = booking.Id, SequenceNumber = number, DueDate = Today.AddDays(-60 + number),
                    Amount = 100_000m, Status = InstallmentStatus.Paid, PaidAt = DateTime.UtcNow.AddDays(-30)
                };
                Context.Installments.Add(paid);
                await Context.SaveChangesAsync();
                Context.Payments.Add(new Payment
                {
                    BookingId = booking.Id, InstallmentId = paid.Id, Type = PaymentType.Installment,
                    Amount = 100_000m, PaymentMethod = PaymentMethod.Cash, PaidAt = DateTime.UtcNow.AddDays(-30)
                });
            }
            for (var i = 0; i < unpaidCount; i++, number++)
            {
                Context.Installments.Add(new Installment
                {
                    BookingId = booking.Id, SequenceNumber = number, DueDate = Today.AddDays(unpaidStartsDaysFromToday + i * 30),
                    Amount = 100_000m, Status = InstallmentStatus.Pending
                });
            }
            booking.NumberOfInstallments = number - 1;
            await Context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
