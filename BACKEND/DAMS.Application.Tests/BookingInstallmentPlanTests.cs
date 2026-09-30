using System.Reflection;
using DAMS.Api.Controllers;
using DAMS.Api.Filters;
using DAMS.Application.Common;
using DAMS.Application.DTOs.InstallmentDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>KAN-74: the plan's first due date, its settings, money outside the plan, and the payment rows.</summary>
public sealed class BookingInstallmentPlanTests
{
    private static readonly DateTime Oct26 = new(2027, 10, 26);

    [Fact]
    public async Task TheDateEntered_IsTheDueDateOfInstallmentOne()
    {
        await using var h = await Harness.Create();

        var monthly = await h.Installments.GenerateScheduleAsync(h.BookingId, h.Plan(3, InstallmentFrequency.Monthly, Oct26), 1);
        Assert.Equal(new[] { Oct26, new DateTime(2027, 11, 26), new DateTime(2027, 12, 26) },
            monthly.Items.OrderBy(i => i.SequenceNumber).Select(i => i.DueDate.Date).ToArray());

        var quarterly = await h.Installments.GenerateScheduleAsync(h.BookingId, h.Plan(3, InstallmentFrequency.Quarterly, Oct26, regenerate: true), 1);
        Assert.Equal(new[] { Oct26, new DateTime(2028, 1, 26), new DateTime(2028, 4, 26) },
            quarterly.Items.OrderBy(i => i.SequenceNumber).Select(i => i.DueDate.Date).ToArray());

        var halfYearly = await h.Installments.GenerateScheduleAsync(h.BookingId, h.Plan(2, InstallmentFrequency.HalfYearly, Oct26, regenerate: true), 1);
        Assert.Equal(new[] { Oct26, new DateTime(2028, 4, 26) }, halfYearly.Items.OrderBy(i => i.SequenceNumber).Select(i => i.DueDate.Date).ToArray());

        var yearly = await h.Installments.GenerateScheduleAsync(h.BookingId, h.Plan(2, InstallmentFrequency.Yearly, Oct26, regenerate: true), 1);
        Assert.Equal(new[] { Oct26, new DateTime(2028, 10, 26) }, yearly.Items.OrderBy(i => i.SequenceNumber).Select(i => i.DueDate.Date).ToArray());
    }

    [Fact]
    public async Task TheEndOfAShortMonth_DoesNotPullEveryLaterInstallmentEarlier()
    {
        await using var h = await Harness.Create();

        var schedule = await h.Installments.GenerateScheduleAsync(h.BookingId, h.Plan(3, InstallmentFrequency.Monthly, new DateTime(2027, 1, 31)), 1);

        Assert.Equal(new[] { new DateTime(2027, 1, 31), new DateTime(2027, 2, 28), new DateTime(2027, 3, 31) },
            schedule.Items.OrderBy(i => i.SequenceNumber).Select(i => i.DueDate.Date).ToArray());
    }

    [Fact]
    public async Task TheSchedule_CarriesThePlanSettings_SoChangePlanCanBePrefilled()
    {
        await using var h = await Harness.Create();
        var dto = h.Plan(4, InstallmentFrequency.Quarterly, Oct26);
        dto.PossessionAmount = 100_000m;
        dto.PossessionDueDate = new DateTime(2029, 1, 15);
        await h.Installments.GenerateScheduleAsync(h.BookingId, dto, 1);

        var schedule = await h.Installments.GetScheduleAsync(h.BookingId);

        Assert.Equal(InstallmentFrequency.Quarterly, schedule.Frequency);
        Assert.Equal(4, schedule.NumberOfInstallments);
        Assert.Equal(Oct26, schedule.InstallmentStartDate);
        Assert.Equal(100_000m, schedule.PossessionAmount);
        Assert.Equal(new DateTime(2029, 1, 15), schedule.PossessionDueDate);
        Assert.True(schedule.CanRegenerate);
    }

    [Fact]
    public async Task APlanBuiltBeforeThisChange_ReportsWhereInstallmentOneReallyFalls()
    {
        await using var h = await Harness.Create();
        await h.Installments.GenerateScheduleAsync(h.BookingId, h.Plan(3, InstallmentFrequency.Monthly, Oct26), 1);
        // The old rule put installment 1 a period AFTER the date that was stored.
        h.Context.Bookings.Single().InstallmentPlanStartDate = Oct26.AddMonths(-1);
        await h.Context.SaveChangesAsync();

        var schedule = await h.Installments.GetScheduleAsync(h.BookingId);

        Assert.Equal(Oct26, schedule.InstallmentStartDate);
    }

    [Fact]
    public async Task MoneyOutsideThePlan_IsReported_WhateverPutItThere_AndPaymentsAreRefusedUntilItIsChanged()
    {
        await using var h = await Harness.Create();
        // The balance is 500,000; this plan only holds 300,000 of it. No rebate is involved.
        h.AddInstallments(300_000m);

        var short_ = await h.Installments.GetScheduleAsync(h.BookingId);
        Assert.Equal(200_000m, short_.UnscheduledBalance);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Installments.RecordInstallmentPaymentAsync(h.BookingId, short_.Items[0].Id, h.Receipt(50_000m), 1));
        Assert.Equal("Change the plan for the current balance first.", refusal.Message);
        Assert.Single(h.Context.Payments);

        var rebuilt = await h.Installments.GenerateScheduleAsync(h.BookingId, h.Plan(2, InstallmentFrequency.Monthly, Oct26, regenerate: true), 1);
        Assert.Equal(0m, rebuilt.UnscheduledBalance);
        Assert.Equal(500_000m, rebuilt.ScheduleTotal);

        await h.Installments.RecordInstallmentPaymentAsync(h.BookingId, rebuilt.Items[0].Id, h.Receipt(50_000m), 1);
        Assert.Equal(2, h.Context.Payments.Count());
    }

    [Fact]
    public async Task APlanThatHoldsTheBalanceExactly_ReportsNothingOutsideIt_EvenPartWayThrough()
    {
        await using var h = await Harness.Create();
        var schedule = await h.Installments.GenerateScheduleAsync(h.BookingId, h.Plan(2, InstallmentFrequency.Monthly, Oct26), 1);
        Assert.Equal(0m, schedule.UnscheduledBalance);

        var paid = await h.Installments.RecordInstallmentPaymentAsync(h.BookingId, schedule.Items[0].Id, h.Receipt(100_000m), 1);

        Assert.Equal(0m, paid.UnscheduledBalance);
        Assert.Equal(400_000m, paid.ScheduleRemaining);
    }

    [Fact]
    public async Task APlanThatHoldsMoreThanTheBalance_IsNotReportedAsMoneyOutsideIt()
    {
        await using var h = await Harness.Create();
        h.AddInstallments(700_000m);

        Assert.Equal(0m, (await h.Installments.GetScheduleAsync(h.BookingId)).UnscheduledBalance);
    }

    [Fact]
    public async Task PaymentRows_NameTheAccount_TheRecorder_AndTheInstallment()
    {
        await using var h = await Harness.Create();
        var schedule = await h.Installments.GenerateScheduleAsync(h.BookingId, h.Plan(2, InstallmentFrequency.Monthly, Oct26), 1);
        await h.Installments.RecordInstallmentPaymentAsync(h.BookingId, schedule.Items.Single(i => i.SequenceNumber == 2).Id, h.Receipt(50_000m), h.UserId);

        var rows = await h.Bookings.GetBookingPaymentsAsync(h.BookingId);

        var installment = rows[0];
        Assert.Equal(PaymentType.Installment, installment.Type);
        Assert.Equal(2, installment.InstallmentSequence);
        Assert.Equal(InstallmentType.Regular, installment.InstallmentType);
        Assert.Equal("Cash in hand", installment.AccountName);
        Assert.Equal(h.AccountId, installment.FinanceAccountId);
        Assert.Equal("Farah Accounts", installment.RecordedByName);

        var bookingAmount = rows[1];
        Assert.Equal(PaymentType.BookingAmount, bookingAmount.Type);
        Assert.Null(bookingAmount.InstallmentSequence);
        Assert.Null(bookingAmount.InstallmentType);
    }

    [Fact]
    public async Task ReplayingOneAttemptToBuildAFirstPlan_ReportsThePlanItBuilt_InsteadOfFailingBecauseItExists()
    {
        await using var h = await Harness.Create();
        var dto = h.Plan(3, InstallmentFrequency.Monthly, Oct26);

        await h.Installments.GenerateScheduleAsync(h.BookingId, dto, 1, "attempt-a");
        // The execution strategy re-running the delegate after a commit whose acknowledgement was lost.
        var replayed = await h.Installments.GenerateScheduleAsync(h.BookingId, dto, 1, "attempt-a");

        Assert.Equal(3, replayed.Items.Count);
        Assert.Equal(3, h.Context.Installments.Count());
        Assert.Single(h.Context.InstallmentPlanAttempts);
    }

    [Fact]
    public async Task AnOldAttemptReplayedAfterSomeoneChangedThePlan_CannotOverwriteTheNewerPlan()
    {
        await using var h = await Harness.Create();
        // A builds the plan and its acknowledgement is lost; B then changes it to 2 quarterly installments.
        await h.Installments.GenerateScheduleAsync(h.BookingId, h.Plan(3, InstallmentFrequency.Monthly, Oct26), 1, "attempt-a");
        await h.Installments.GenerateScheduleAsync(h.BookingId, h.Plan(2, InstallmentFrequency.Quarterly, Oct26, regenerate: true), 2, "attempt-b");

        // A's retry arrives now, asking to replace the plan again.
        var replayed = await h.Installments.GenerateScheduleAsync(
            h.BookingId, h.Plan(3, InstallmentFrequency.Monthly, Oct26, regenerate: true), 1, "attempt-a");

        Assert.Equal(2, replayed.Items.Count);
        Assert.Equal(InstallmentFrequency.Quarterly, replayed.Frequency);
        Assert.Equal(2, h.Context.Installments.Count());
        Assert.Equal(2, h.Context.InstallmentPlanAttempts.Count());
    }

    [Fact]
    public async Task ASecondDeliberateAttempt_IsStillRefusedWhileAPlanExists()
    {
        await using var h = await Harness.Create();
        var dto = h.Plan(3, InstallmentFrequency.Monthly, Oct26);
        await h.Installments.GenerateScheduleAsync(h.BookingId, dto, 1, "attempt-a");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Installments.GenerateScheduleAsync(h.BookingId, dto, 1, "attempt-b"));

        Assert.Contains("already exists", error.Message);
    }

    [Fact]
    public void AnOmittedFrequencyOrFirstDueDate_IsRejectedByTheRequestValidation_NotDefaultedToMonthlyOrYearOne()
    {
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var body = new GenerateInstallmentPlanDto { AgreedSalePrice = 1m, NumberOfInstallments = 1 };

        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            body, new System.ComponentModel.DataAnnotations.ValidationContext(body), results, validateAllProperties: true);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(GenerateInstallmentPlanDto.Frequency)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(GenerateInstallmentPlanDto.InstallmentStartDate)));
    }

    [Fact]
    public async Task AnUndefinedFrequencyOrAMissingFirstDueDate_IsRefusedByTheService()
    {
        await using var h = await Harness.Create();

        var badFrequency = h.Plan(3, (InstallmentFrequency)99, Oct26);
        Assert.Contains("how often", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Installments.GenerateScheduleAsync(h.BookingId, badFrequency, 1))).Message);

        var noFrequency = h.Plan(3, InstallmentFrequency.Monthly, Oct26);
        noFrequency.Frequency = null;
        Assert.Contains("how often", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Installments.GenerateScheduleAsync(h.BookingId, noFrequency, 1))).Message);

        var noDate = h.Plan(3, InstallmentFrequency.Monthly, Oct26);
        noDate.InstallmentStartDate = null;
        Assert.Contains("First due date is required", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Installments.GenerateScheduleAsync(h.BookingId, noDate, 1))).Message);
        Assert.Empty(h.Context.Installments);
    }

    [Fact]
    public void CreatingOrChangingThePlan_IsRetryKeyed()
    {
        var action = typeof(BookingController).GetMethod(nameof(BookingController.GenerateInstallmentPlan))!;

        Assert.NotNull(action.GetCustomAttribute<IdempotentMoneyOperationAttribute>());
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly AppDbContext _context;
        public AppDbContext Context => _context;
        public InstallmentService Installments { get; }
        public BookingService Bookings { get; }
        public int BookingId { get; private set; }
        public int AccountId { get; private set; }
        public int UserId { get; private set; }

        private Harness(AppDbContext context)
        {
            _context = context;
            Installments = new InstallmentService(context, new FinanceAccountService(context));
            Bookings = new BookingService(context, new CustomerService(context), new FinanceAccountService(context));
        }

        public static async Task<Harness> Create()
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            context.Database.EnsureCreated();
            var h = new Harness(context);

            var project = new Project { ProjectName = "Floria Heights", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit { Project = project, UnitNumber = "B08", UnitType = "2 Bed", Price = 1_000_000m, Status = UnitStatus.OnPaymentPlan };
            var customer = new Customer { FullName = "Usman Tariq", Phone = "03334412987", Status = CustomerStatus.Active };
            var booking = new Booking
            {
                BookingReference = "BK-000013", Customer = customer, Unit = unit, Status = BookingStatus.PaymentPlanActive,
                Source = CustomerSource.Referral, ListPrice = 1_000_000m, AgreedSalePrice = 1_000_000m, DiscountAmount = 0m,
                BookingAmountRequired = 500_000m, BookingAmountReceived = 500_000m, BookingDate = DateTime.UtcNow.AddDays(-20)
            };
            var account = new FinanceAccount { Name = "Cash in hand", Type = FinanceAccountType.Cash, AccountHolderName = "Head office", IsActive = true };
            var user = new User { FullName = "Farah Accounts", Email = "farah@dams.test", NormalizedEmail = "FARAH@DAMS.TEST", Password = "hash", RoleId = 5 };
            booking.Payments.Add(new Payment
            {
                Booking = booking, Amount = 500_000m, Type = PaymentType.BookingAmount, PaymentMethod = PaymentMethod.Cash,
                FinanceAccount = account, PaidAt = DateTime.UtcNow.AddDays(-15)
            });
            context.AddRange(project, unit, customer, booking, account, user);
            await context.SaveChangesAsync();
            h.BookingId = booking.Id;
            h.AccountId = account.Id;
            h.UserId = user.UserId;
            return h;
        }

        public GenerateInstallmentPlanDto Plan(int count, InstallmentFrequency frequency, DateTime firstDue, bool regenerate = false) => new()
        {
            AgreedSalePrice = 1_000_000m, DiscountPercent = 0m, Frequency = frequency, NumberOfInstallments = count,
            InstallmentStartDate = firstDue, Regenerate = regenerate
        };

        public RecordInstallmentPaymentDto Receipt(decimal amount) => new()
        {
            Amount = amount, PaymentMethod = PaymentMethod.Cash, FinanceAccountId = AccountId
        };

        /// <summary>One plan by hand, whose rows total <paramref name="total"/> whatever the balance is.</summary>
        public void AddInstallments(decimal total)
        {
            var booking = Context.Bookings.Single();
            Context.Installments.AddRange(
                new Installment { BookingId = booking.Id, SequenceNumber = 1, DueDate = Oct26, Amount = total / 2, Status = InstallmentStatus.Pending },
                new Installment { BookingId = booking.Id, SequenceNumber = 2, DueDate = Oct26.AddMonths(1), Amount = total / 2, Status = InstallmentStatus.Pending });
            booking.NumberOfInstallments = 2;
            Context.SaveChanges();
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
