using DAMS.Application.DTOs.CustomerDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Domain.Identity;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// Who may read a booking, and the one chain that answers it:
/// <c>token NameIdentifier → Customer.UserId → Booking.CustomerId → installments / payments /
/// receipts</c>.
///
/// <para>
/// Every test uses two real client accounts with the same role, and has one of them ask for the
/// other's data by id. Testing anonymous-versus-authenticated would prove nothing here: both
/// people are legitimately signed in, and the bug being closed was never about authentication.
/// It was that a matching email address counted as proof of ownership — so several of these
/// deliberately arrange for the addresses to match and assert that it changes nothing.
/// </para>
/// </summary>
public sealed class CustomerPortalOwnershipTests
{
    // ── The ownership chain ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_client_sees_their_own_bookings()
    {
        await using var h = await Harness.CreateAsync();

        var mine = await h.Bookings.GetBookingsForUserAsync(h.UserA);

        Assert.Equal(h.BookingA, Assert.Single(mine).Id);
    }

    [Fact]
    public async Task A_client_cannot_list_or_open_another_clients_booking()
    {
        await using var h = await Harness.CreateAsync();

        Assert.DoesNotContain(await h.Bookings.GetBookingsForUserAsync(h.UserA), b => b.Id == h.BookingB);

        // Null, which the controller turns into 404 — the same answer as an id that does not
        // exist, so walking the id space tells the caller nothing.
        Assert.Null(await h.Bookings.GetBookingForUserAsync(h.BookingB, h.UserA));
        Assert.False(await h.Bookings.UserOwnsBookingAsync(h.BookingB, h.UserA));
    }

    [Fact]
    public async Task The_gate_on_schedules_payments_and_receipts_is_the_same_one()
    {
        await using var h = await Harness.CreateAsync();

        // Every client-facing resource hanging off a booking passes through UserOwnsBookingAsync
        // before it is read. Asserting the gate itself is what keeps a future endpoint from
        // inventing its own weaker version.
        Assert.True(await h.Bookings.UserOwnsBookingAsync(h.BookingA, h.UserA));
        Assert.False(await h.Bookings.UserOwnsBookingAsync(h.BookingA, h.UserB));
        Assert.False(await h.Bookings.UserOwnsBookingAsync(h.BookingB, h.UserA));
        Assert.True(await h.Bookings.UserOwnsBookingAsync(h.BookingB, h.UserB));
    }

    [Fact]
    public async Task A_receipt_id_must_belong_to_the_booking_that_was_authorized()
    {
        await using var h = await Harness.CreateAsync();

        // User A legitimately owns booking A, so the first check passes. The second one — that the
        // payment belongs to that booking — is what stops them walking the payment id space and
        // pulling B's receipt through their own booking.
        Assert.True(await h.Bookings.UserOwnsBookingAsync(h.BookingA, h.UserA));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Bookings.GetPaymentReceiptAsync(h.BookingA, h.PaymentB));

        // Their own receipt still comes back.
        var mine = await h.Bookings.GetPaymentReceiptAsync(h.BookingA, h.PaymentA);
        Assert.Equal(h.PaymentA, mine.PaymentId);
    }

    // ── Email is not a key ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_matching_email_on_an_unowned_customer_grants_nothing()
    {
        await using var h = await Harness.CreateAsync();

        // The original exploit, exactly: a historical customer carrying a buyer's address, with no
        // login attached, and somebody who has registered that same address.
        var orphan = await h.AddCustomerAsync("Historic Buyer", email: "shared@example.com", userId: null);
        var orphanBooking = await h.AddBookingAsync(orphan);
        await h.SetUserEmailAsync(h.UserA, "shared@example.com");

        Assert.False(await h.Bookings.UserOwnsBookingAsync(orphanBooking, h.UserA));
        Assert.Null(await h.Bookings.GetBookingForUserAsync(orphanBooking, h.UserA));
        Assert.DoesNotContain(await h.Bookings.GetBookingsForUserAsync(h.UserA), b => b.Id == orphanBooking);
    }

    [Fact]
    public async Task A_matching_email_on_someone_elses_customer_grants_nothing()
    {
        await using var h = await Harness.CreateAsync();

        // Customer B belongs to User B. User A's address happens to match it — a reassigned
        // corporate mailbox, a shared family address, or simply a stranger who registered it.
        await h.SetCustomerEmailAsync(h.CustomerB, "shared@example.com");
        await h.SetUserEmailAsync(h.UserA, "shared@example.com");

        Assert.False(await h.Bookings.UserOwnsBookingAsync(h.BookingB, h.UserA));
        Assert.Null(await h.Bookings.GetBookingForUserAsync(h.BookingB, h.UserA));

        // And B has lost nothing by the collision.
        Assert.True(await h.Bookings.UserOwnsBookingAsync(h.BookingB, h.UserB));
    }

    [Fact]
    public async Task A_linked_customer_is_owned_even_when_the_addresses_have_nothing_in_common()
    {
        await using var h = await Harness.CreateAsync();

        // The inverse, and the reason the old email fallback was never necessary: the link is what
        // ownership is, so the contact address is free to be anything at all.
        await h.SetCustomerEmailAsync(h.CustomerA, "someone.entirely.else@example.com");

        Assert.True(await h.Bookings.UserOwnsBookingAsync(h.BookingA, h.UserA));
        Assert.Equal(h.BookingA, Assert.Single(await h.Bookings.GetBookingsForUserAsync(h.UserA)).Id);
    }

    [Fact]
    public async Task Changing_a_customers_contact_email_moves_no_bookings()
    {
        await using var h = await Harness.CreateAsync();

        await h.Customers.UpdateCustomerAsync(h.CustomerA, new UpdateCustomerDto
        {
            FullName = "Aisha Ahmed",
            Phone = "03001110001",
            Email = "brand.new.address@example.com",
            Status = CustomerStatus.Active
        });
        h.Db.ChangeTracker.Clear();

        // Ownership survives the change...
        var customer = await h.Db.Customers.AsNoTracking().FirstAsync(c => c.Id == h.CustomerA);
        Assert.Equal(h.UserA, customer.UserId);
        Assert.True(await h.Bookings.UserOwnsBookingAsync(h.BookingA, h.UserA));

        // ...and nobody else acquires it by holding either the old address or the new one.
        await h.SetUserEmailAsync(h.UserB, "brand.new.address@example.com");
        Assert.False(await h.Bookings.UserOwnsBookingAsync(h.BookingA, h.UserB));
    }

    // ── Who may own anything at all ─────────────────────────────────────────────

    [Fact]
    public async Task An_unverified_client_cannot_be_given_a_customer_record()
    {
        await using var h = await Harness.CreateAsync();
        var orphan = await h.AddCustomerAsync("Historic Buyer", email: "historic@example.com", userId: null);

        var pending = await h.AddUserAsync("pending@example.com", UserAccountStatus.PendingEmailVerification, verified: false);
        var unverifiedActive = await h.AddUserAsync("legacy@example.com", UserAccountStatus.Active, verified: false);

        foreach (var candidate in new[] { pending, unverifiedActive })
        {
            var result = await h.Links.LinkByAdministratorAsync(
                orphan, candidate, h.AdminUser, "Identity confirmed at the Karachi office.");

            Assert.Equal(CustomerAccountLinkOutcome.AccountNotEligible, result.Outcome);
        }

        Assert.Null((await h.Db.Customers.AsNoTracking().FirstAsync(c => c.Id == orphan)).UserId);

        // Both refusals are on the record, not merely absent from it.
        Assert.Equal(2, await h.Db.CustomerAccountLinkAudits
            .CountAsync(a => a.Action == CustomerAccountLinkAction.RejectedNotEligible));
    }

    // ── Conversion preserves the authenticated identity ─────────────────────────

    [Fact]
    public async Task A_new_customer_from_a_verified_request_is_linked_to_its_submitter()
    {
        await using var h = await Harness.CreateAsync();
        var customer = await h.AddCustomerAsync("Fresh Buyer", email: "fresh@example.com", userId: null);
        var request = await h.AddBookingRequestAsync(h.UserA, email: "typed-something-else@example.com");

        var result = await h.Links.LinkNewCustomerFromBookingRequestAsync(customer, request);

        Assert.Equal(CustomerAccountLinkOutcome.Linked, result.Outcome);
        Assert.Equal(h.UserA, (await h.Db.Customers.AsNoTracking().FirstAsync(c => c.Id == customer)).UserId);

        // The identity came from the stored request, not from the address on it — which here
        // deliberately matches nothing.
        var audit = await h.Db.CustomerAccountLinkAudits.AsNoTracking()
            .SingleAsync(a => a.CustomerId == customer);
        Assert.Equal(CustomerAccountLinkAction.LinkedFromBookingRequest, audit.Action);
        Assert.Equal(request, audit.BookingRequestId);
        Assert.Equal(h.UserA, audit.ResultingUserId);
    }

    [Fact]
    public async Task A_request_submitted_while_signed_out_links_nobody()
    {
        await using var h = await Harness.CreateAsync();
        var customer = await h.AddCustomerAsync("Walk-in Buyer", email: "walkin@example.com", userId: null);
        var anonymous = await h.AddBookingRequestAsync(userId: null, email: "walkin@example.com");

        var result = await h.Links.LinkNewCustomerFromBookingRequestAsync(customer, anonymous);

        // The email matches perfectly and it still grants nothing, because there was never an
        // authenticated identity to preserve.
        Assert.Equal(CustomerAccountLinkOutcome.BookingRequestNotEligible, result.Outcome);
        Assert.Null((await h.Db.Customers.AsNoTracking().FirstAsync(c => c.Id == customer)).UserId);
    }

    [Fact]
    public async Task An_existing_customer_is_not_claimed_by_a_matching_email_or_cnic()
    {
        await using var h = await Harness.CreateAsync();

        // The record already exists with this person's email and CNIC on it. Deduplication finds
        // it — which is right, DAMS must not create a second one — and that is all it does.
        var historic = await h.AddCustomerAsync(
            "Historic Buyer", email: "historic@example.com", userId: null, cnic: "42101-1234567-1");

        var resolution = await h.Customers.FindOrCreateCustomerAsync(
            "Historic Buyer", "03009998887", "42101-1234567-1", "historic@example.com",
            address: null, CustomerSource.Website, sourceNotes: null, createdByUserId: h.AdminUser);
        h.Db.ChangeTracker.Clear();

        Assert.Equal(historic, resolution.CustomerId);
        Assert.False(resolution.WasCreated);

        // No login attached, by either identifier, and therefore no portal access.
        Assert.Null((await h.Db.Customers.AsNoTracking().FirstAsync(c => c.Id == historic)).UserId);
    }

    [Fact]
    public async Task An_existing_link_is_never_replaced_automatically()
    {
        await using var h = await Harness.CreateAsync();
        var request = await h.AddBookingRequestAsync(h.UserA, email: "aisha@example.com");

        // Customer B belongs to User B. A conversion claiming it was created for A's request must
        // not move it, whatever else lines up.
        var conversion = await h.Links.LinkNewCustomerFromBookingRequestAsync(h.CustomerB, request);
        Assert.Equal(CustomerAccountLinkOutcome.Conflict, conversion.Outcome);

        // Nor may an administrator do it by accident. The default refuses and says why.
        var admin = await h.Links.LinkByAdministratorAsync(
            h.CustomerB, h.UserA, h.AdminUser, "Customer says the account is theirs.");
        Assert.Equal(CustomerAccountLinkOutcome.Conflict, admin.Outcome);
        Assert.Equal(h.UserB, admin.LinkedUserId);

        Assert.Equal(h.UserB, (await h.Db.Customers.AsNoTracking().FirstAsync(c => c.Id == h.CustomerB)).UserId);
        Assert.True(await h.Bookings.UserOwnsBookingAsync(h.BookingB, h.UserB));
        Assert.False(await h.Bookings.UserOwnsBookingAsync(h.BookingB, h.UserA));

        // Both refusals are visible afterwards, which is the point of recording them.
        Assert.Equal(2, await h.Db.CustomerAccountLinkAudits
            .CountAsync(a => a.CustomerId == h.CustomerB
                          && a.Action == CustomerAccountLinkAction.RejectedConflict));
    }

    // ── The trusted claim path ──────────────────────────────────────────────────

    [Fact]
    public async Task An_administrator_can_claim_an_unowned_customer_and_it_is_recorded()
    {
        await using var h = await Harness.CreateAsync();
        var historic = await h.AddCustomerAsync("Historic Buyer", email: "historic@example.com", userId: null);
        var booking = await h.AddBookingAsync(historic);

        Assert.False(await h.Bookings.UserOwnsBookingAsync(booking, h.UserA));

        var result = await h.Links.LinkByAdministratorAsync(
            historic, h.UserA, h.AdminUser, "CNIC and passport checked in person at the Karachi office.");

        Assert.Equal(CustomerAccountLinkOutcome.Linked, result.Outcome);
        Assert.True(await h.Bookings.UserOwnsBookingAsync(booking, h.UserA));

        var audit = Assert.Single(await h.Links.GetHistoryAsync(historic));
        Assert.Equal(CustomerAccountLinkAction.LinkedByAdministrator, audit.Action);
        Assert.Equal(h.AdminUser, audit.PerformedByUserId);
        Assert.Equal("CNIC and passport checked in person at the Karachi office.", audit.Reason);
    }

    [Fact]
    public async Task An_explicit_reassignment_moves_ownership_and_leaves_both_ends_on_the_record()
    {
        await using var h = await Harness.CreateAsync();

        var result = await h.Links.LinkByAdministratorAsync(
            h.CustomerB, h.UserA, h.AdminUser,
            "Duplicate signup; the record was attached to the wrong login at conversion.",
            allowReassign: true);

        Assert.Equal(CustomerAccountLinkOutcome.Reassigned, result.Outcome);
        Assert.True(await h.Bookings.UserOwnsBookingAsync(h.BookingB, h.UserA));
        Assert.False(await h.Bookings.UserOwnsBookingAsync(h.BookingB, h.UserB));

        var audit = Assert.Single(await h.Links.GetHistoryAsync(h.CustomerB));
        Assert.Equal(CustomerAccountLinkAction.ReassignedByAdministrator, audit.Action);
        Assert.Equal(h.UserB, audit.PreviousUserId);
        Assert.Equal(h.UserA, audit.ResultingUserId);
    }

    [Fact]
    public async Task Unlinking_withdraws_access_and_is_audited()
    {
        await using var h = await Harness.CreateAsync();

        var result = await h.Links.UnlinkByAdministratorAsync(
            h.CustomerA, h.AdminUser, "Account holder reported the login as compromised.");

        Assert.Equal(CustomerAccountLinkOutcome.Unlinked, result.Outcome);
        Assert.False(await h.Bookings.UserOwnsBookingAsync(h.BookingA, h.UserA));
        Assert.Empty(await h.Bookings.GetBookingsForUserAsync(h.UserA));

        var audit = Assert.Single(await h.Links.GetHistoryAsync(h.CustomerA));
        Assert.Equal(CustomerAccountLinkAction.UnlinkedByAdministrator, audit.Action);
    }

    /// <summary>
    /// Two client accounts of the same role, each owning one customer with one booking and one
    /// payment, plus an admin. Everything a cross-user test needs and nothing it does not.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public BookingService Bookings { get; }
        public CustomerService Customers { get; }
        public CustomerAccountLinkService Links { get; }

        public int UserA { get; private set; }
        public int UserB { get; private set; }
        public int AdminUser { get; private set; }
        public int CustomerA { get; private set; }
        public int CustomerB { get; private set; }
        public int BookingA { get; private set; }
        public int BookingB { get; private set; }
        public int PaymentA { get; private set; }
        public int PaymentB { get; private set; }

        private int _unitSequence;

        private Harness(AppDbContext db)
        {
            Db = db;
            Customers = new CustomerService(db);
            Bookings = new BookingService(db, Customers, new FinanceAccountService(db));
            Links = new CustomerAccountLinkService(db, TimeProvider.System);
        }

        public static async Task<Harness> CreateAsync()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                // CustomerService opens a serialisable transaction around deduplication, which the
                // in-memory provider cannot honour. Nothing under test here depends on isolation;
                // the transactional behaviour is proved against real SQL Server elsewhere.
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
            var h = new Harness(db);

            db.Roles.AddRange(
                new Role { RoleId = 1, Role_name = "Admin" },
                new Role { RoleId = 2, Role_name = "Client" });
            await db.SaveChangesAsync();

            h.AdminUser = await h.AddUserAsync("admin@dams.test", UserAccountStatus.Active, verified: false, roleId: 1);
            h.UserA = await h.AddUserAsync("aisha@example.com", UserAccountStatus.Active, verified: true);
            h.UserB = await h.AddUserAsync("bilal@example.com", UserAccountStatus.Active, verified: true);

            h.CustomerA = await h.AddCustomerAsync("Aisha Ahmed", "aisha@example.com", h.UserA);
            h.CustomerB = await h.AddCustomerAsync("Bilal Baig", "bilal@example.com", h.UserB);

            h.BookingA = await h.AddBookingAsync(h.CustomerA);
            h.BookingB = await h.AddBookingAsync(h.CustomerB);

            h.PaymentA = await h.AddPaymentAsync(h.BookingA);
            h.PaymentB = await h.AddPaymentAsync(h.BookingB);

            db.ChangeTracker.Clear();
            return h;
        }

        public async Task<int> AddUserAsync(
            string email, UserAccountStatus status, bool verified, int roleId = 2)
        {
            var user = new User
            {
                RoleId = roleId,
                FullName = email,
                Email = email,
                NormalizedEmail = EmailIdentity.Normalize(email),
                Password = BCrypt.Net.BCrypt.HashPassword("irrelevant-here-1"),
                AccountStatus = status,
                EmailVerifiedAt = verified ? new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) : null
            };
            Db.Users.Add(user);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return user.UserId;
        }

        public async Task<int> AddCustomerAsync(
            string name, string? email, int? userId, string? cnic = null)
        {
            var customer = new Customer
            {
                FullName = name,
                Phone = $"0300{Random.Shared.Next(1000000, 9999999)}",
                Email = email,
                CNIC = cnic,
                UserId = userId,
                Status = CustomerStatus.Active
            };
            Db.Customers.Add(customer);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return customer.Id;
        }

        public async Task<int> AddBookingAsync(int customerId)
        {
            var project = new Project { ProjectName = $"P{++_unitSequence}", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit
            {
                Project = project,
                UnitNumber = $"U-{_unitSequence:D3}",
                UnitType = "Apartment",
                Price = 5_000_000m,
                Status = UnitStatus.OnPaymentPlan
            };
            var booking = new Booking
            {
                BookingReference = $"BK-{_unitSequence:D4}",
                CustomerId = customerId,
                Unit = unit,
                Status = BookingStatus.PaymentPlanActive,
                ListPrice = 5_000_000m,
                AgreedSalePrice = 5_000_000m,
                BookingDate = new DateTime(2026, 3, 1)
            };
            Db.Bookings.Add(booking);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return booking.Id;
        }

        public async Task<int> AddPaymentAsync(int bookingId)
        {
            var payment = new Payment
            {
                BookingId = bookingId,
                Amount = 250_000m,
                Type = PaymentType.BookingAmount,
                PaymentMethod = PaymentMethod.BankTransfer,
                ReceiptNumber = $"RCPT-{bookingId:D5}",
                PaidAt = new DateTime(2026, 3, 5)
            };
            Db.Payments.Add(payment);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return payment.Id;
        }

        public async Task<int> AddBookingRequestAsync(int? userId, string email)
        {
            var project = new Project { ProjectName = $"RQ{++_unitSequence}", Location = "Karachi", CreatedById = 1 };
            var unit = new Unit
            {
                Project = project,
                UnitNumber = $"RQ-{_unitSequence:D3}",
                UnitType = "Apartment",
                Price = 5_000_000m,
                Status = UnitStatus.Available
            };
            var request = new BookingRequest
            {
                Unit = unit,
                UserId = userId,
                FullName = "Requester",
                Phone = "03001234567",
                Email = email,
                CNIC = "42101-0000000-0",
                Address = "Karachi",
                Status = BookingRequestStatus.Pending
            };
            Db.BookingRequests.Add(request);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return request.Id;
        }

        public async Task SetUserEmailAsync(int userId, string email)
        {
            var user = await Db.Users.FirstAsync(u => u.UserId == userId);
            user.Email = email;
            user.NormalizedEmail = EmailIdentity.Normalize(email);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task SetCustomerEmailAsync(int customerId, string email)
        {
            var customer = await Db.Customers.FirstAsync(c => c.Id == customerId);
            customer.Email = email;
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
