using System.Text;
using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingDtos;
using DAMS.Application.DTOs.CommissionRebateDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>Proof files on customer payments and cancellation refunds reuse the commission / rebate evidence mechanism.</summary>
public sealed class PaymentAndRefundProofTests
{
    private static readonly FinancialWorkflowActor Actor = new(42, "Finance Admin");
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n");

    [Fact]
    public async Task PaymentProof_IsStoredAuditedDownloadableAndShownOnTheBooking()
    {
        await using var h = await Harness.Create();
        var paymentId = h.Context.Payments.Single().Id;

        var uploaded = await h.Upload(FinancialEvidenceOwnerType.CustomerPayment, paymentId);

        var evidence = Assert.Single(h.Context.FinancialEvidence);
        Assert.Equal(paymentId, evidence.CustomerPaymentId);
        Assert.Null(evidence.CancellationRefundId);
        Assert.DoesNotContain("/", evidence.StoredFileName);
        Assert.Equal("proof.pdf", uploaded.OriginalFileName);
        Assert.Contains(h.Context.FinancialWorkflowAuditEntries,
            a => a.Action == FinancialWorkflowAction.EvidenceUploaded && a.BookingId == h.BookingId);

        var download = await h.Evidence.DownloadEvidenceAsync(evidence.Id, Actor);
        await download.Content.DisposeAsync();
        Assert.Contains(h.Context.FinancialWorkflowAuditEntries, a => a.Action == FinancialWorkflowAction.EvidenceDownloaded);

        var detail = await h.Bookings.GetBookingByIdAsync(h.BookingId);
        var proof = Assert.Single(detail!.Payments).Proof;
        Assert.NotNull(proof);
        Assert.Equal(evidence.Id, proof!.Id);
        Assert.Equal("proof.pdf", proof.FileName);
        Assert.Equal(Pdf.Length, proof.FileSize);

        var listed = Assert.Single(await h.Bookings.GetBookingPaymentsAsync(h.BookingId));
        Assert.Equal(evidence.Id, listed.Proof!.Id);
    }

    [Fact]
    public async Task PaymentWithoutProof_HasNoProofOnTheBooking()
    {
        await using var h = await Harness.Create();
        var detail = await h.Bookings.GetBookingByIdAsync(h.BookingId);
        Assert.Null(Assert.Single(detail!.Payments).Proof);
    }

    [Fact]
    public async Task PaymentProof_AcceptsOnlyOneFile()
    {
        await using var h = await Harness.Create();
        var paymentId = h.Context.Payments.Single().Id;
        await h.Upload(FinancialEvidenceOwnerType.CustomerPayment, paymentId);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Upload(FinancialEvidenceOwnerType.CustomerPayment, paymentId));

        Assert.Contains("already has proof", error.Message);
        Assert.Single(h.Context.FinancialEvidence);
        Assert.Single(h.Storage.Files);
    }

    [Fact]
    public async Task PaymentProof_RejectsUnknownPaymentAndBadFiles()
    {
        await using var h = await Harness.Create();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => h.Upload(FinancialEvidenceOwnerType.CustomerPayment, 9999));

        var paymentId = h.Context.Payments.Single().Id;
        var bad = Encoding.UTF8.GetBytes("MZ executable");
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Evidence.UploadEvidenceAsync(
            FinancialEvidenceOwnerType.CustomerPayment, paymentId,
            new FinancialEvidenceUpload { Content = new MemoryStream(bad), FileName = "proof.pdf", Length = bad.Length }, Actor));
        Assert.Empty(h.Context.FinancialEvidence);
        Assert.Empty(h.Storage.Files);
    }

    [Fact]
    public async Task RefundProof_IsStoredAuditedAndShownOnTheCancelledBooking()
    {
        await using var h = await Harness.Create();
        var cancelled = await h.Bookings.CancelBookingAsync(h.BookingId, h.PayNowCancellation(), Actor);
        var refundId = cancelled.CancellationSettlement!.Refund!.Id;
        Assert.Null(cancelled.CancellationSettlement.Refund.Proof);

        await h.Upload(FinancialEvidenceOwnerType.CancellationRefund, refundId);

        var evidence = Assert.Single(h.Context.FinancialEvidence);
        Assert.Equal(refundId, evidence.CancellationRefundId);
        Assert.Null(evidence.CustomerPaymentId);
        Assert.Contains(h.Context.FinancialWorkflowAuditEntries,
            a => a.Action == FinancialWorkflowAction.EvidenceUploaded && a.BookingId == h.BookingId);

        var detail = await h.Bookings.GetBookingByIdAsync(h.BookingId);
        var proof = detail!.CancellationSettlement!.Refund!.Proof;
        Assert.Equal(evidence.Id, proof!.Id);
        Assert.Equal("proof.pdf", proof.FileName);

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Upload(FinancialEvidenceOwnerType.CancellationRefund, refundId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => h.Upload(FinancialEvidenceOwnerType.CancellationRefund, 9999));
    }

    [Fact]
    public async Task CustomerOwnCopyOfTheBooking_NeverNamesAProofFile()
    {
        await using var h = await Harness.Create();
        await h.Upload(FinancialEvidenceOwnerType.CustomerPayment, h.Context.Payments.Single().Id);

        var own = await h.Bookings.GetBookingForUserAsync(h.BookingId, Harness.CustomerUserId);

        Assert.Null(Assert.Single(own!.Payments).Proof);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public const int CustomerUserId = 7;
        public AppDbContext Context { get; }
        public BookingService Bookings { get; }
        public CommissionRebateService Evidence { get; }
        public MemoryStorage Storage { get; }
        public int BookingId { get; private set; }
        public int CashAccountId { get; private set; }

        private Harness(AppDbContext context, MemoryStorage storage)
        {
            Context = context;
            Storage = storage;
            var accounts = new FinanceAccountService(context);
            Evidence = new CommissionRebateService(context, accounts, storage);
            Bookings = new BookingService(context, new CustomerService(context), accounts,
                notifications: null, commissionLifecycle: Evidence);
        }

        public static async Task<Harness> Create()
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            context.Database.EnsureCreated();
            var harness = new Harness(context, new MemoryStorage());

            var project = new Project { ProjectName = "Proof Test", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit { Project = project, UnitNumber = "P-01", UnitType = "Apartment", Price = 1_000_000m, Status = UnitStatus.OnPaymentPlan };
            var customer = new Customer { FullName = "Buyer", Phone = "03001112222", Status = CustomerStatus.Active, UserId = CustomerUserId };
            var booking = new Booking
            {
                BookingReference = $"BK-{Guid.NewGuid():N}", Customer = customer, Unit = unit,
                Status = BookingStatus.PaymentPlanActive, Source = CustomerSource.Referral,
                AgreedSalePrice = 1_000_000m, DiscountAmount = 0m, BookingAmountRequired = 500_000m,
                BookingAmountReceived = 500_000m, BookingDate = DateTime.UtcNow
            };
            booking.Payments.Add(new Payment { Booking = booking, Amount = 500_000m, Type = PaymentType.BookingAmount, PaymentMethod = PaymentMethod.Cash });
            var cash = new FinanceAccount { Name = "Cash Account", Type = FinanceAccountType.Cash, AccountHolderName = "Company", IsActive = true };
            context.AddRange(project, unit, customer, booking, cash);
            await context.SaveChangesAsync();
            harness.BookingId = booking.Id;
            harness.CashAccountId = cash.Id;
            return harness;
        }

        public Task<FinancialEvidenceDto> Upload(FinancialEvidenceOwnerType type, int ownerId) =>
            Evidence.UploadEvidenceAsync(type, ownerId,
                new FinancialEvidenceUpload { Content = new MemoryStream(Pdf), FileName = "proof.pdf", Length = Pdf.Length }, Actor);

        public CancelBookingDto PayNowCancellation() => new()
        {
            Reason = "Customer requested cancellation",
            ExpectedCustomerCashReceived = 500_000m,
            RefundAmount = 500_000m,
            RefundDecision = CancellationRefundDecision.PayNow,
            IdempotencyKey = "cancel-proof",
            RefundFinanceAccountId = CashAccountId,
            RefundPaymentMethod = PaymentMethod.Cash,
            RefundPaidAt = PakistanTime.Today
        };

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }

    private sealed class MemoryStorage : IFinancialEvidenceStorage
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
        {
            var key = $"{Guid.NewGuid():N}{extension}";
            content.Position = 0;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Files[key] = buffer.ToArray();
            return key;
        }

        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storedFileName, out var value) ? new MemoryStream(value) : null);

        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default)
        {
            Files.Remove(storedFileName);
            return Task.CompletedTask;
        }
    }
}
