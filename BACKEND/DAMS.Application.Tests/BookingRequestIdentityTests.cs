using DAMS.Application.DTOs.BookingRequestDtos;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// The identity chain through the real website approval path — enquiry, lead, conversion,
/// customer, booking — rather than through the link service in isolation.
///
/// <para>
/// This is where the story's "preserve UserId through the website booking flow" either holds or
/// does not. <c>BookingRequest.UserId</c> is captured from the submitter's own token at the moment
/// they were signed in, and it is the only proof of identity anywhere in the conversion; the email
/// on the request is whatever they typed into a form. So every test here puts a misleading email
/// on the request and asserts that the outcome is decided by the stored user id alone.
/// </para>
/// </summary>
public sealed class BookingRequestIdentityTests
{
    [Fact]
    public async Task Approving_a_signed_in_enquiry_links_the_new_customer_to_its_submitter()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var request = await h.BookingRequests.CreateBookingRequestAsync(
            Enquiry(h.UnitId, "someone.else@example.com"), h.ClientUserId);

        var approved = await h.BookingRequests.ApproveBookingRequestAsync(request.Id, h.AdminUserId);
        Assert.Equal(BookingRequestStatus.Approved, approved.Status);

        var customerId = await ConvertedCustomerIdAsync(h, request.Id);
        var customer = await h.Db.Customers.AsNoTracking().SingleAsync(c => c.Id == customerId);

        // The link came from the stored request. The address on it belongs to nobody in
        // particular, and matching played no part.
        Assert.Equal(h.ClientUserId, customer.UserId);
        Assert.Equal("someone.else@example.com", customer.Email);

        var audit = await h.Db.CustomerAccountLinkAudits.AsNoTracking()
            .SingleAsync(a => a.CustomerId == customer.Id);
        Assert.Equal(CustomerAccountLinkAction.LinkedFromBookingRequest, audit.Action);
        Assert.Equal(request.Id, audit.BookingRequestId);
        Assert.Equal(h.ClientUserId, audit.ResultingUserId);

        // And the whole chain now holds end to end: the booking is reachable from the login.
        var booking = await h.Db.Bookings.AsNoTracking().SingleAsync(b => b.CustomerId == customer.Id);
        var mine = await h.Db.Bookings.AsNoTracking()
            .Include(b => b.Customer)
            .Where(b => b.Customer!.UserId == h.ClientUserId)
            .Select(b => b.Id)
            .ToListAsync();
        Assert.Equal([booking.Id], mine);
    }

    [Fact]
    public async Task Approving_an_enquiry_that_matches_an_existing_customer_claims_nothing()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        // A historical CRM record with this person's exact email and CNIC on it, owned by nobody.
        var historic = new Domain.Entities.Customer
        {
            FullName = "Historic Buyer",
            Phone = "03007654321",
            Email = "buyer@example.com",
            CNIC = "42101-7654321-1",
            Status = CustomerStatus.Active
        };
        h.Db.Customers.Add(historic);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var request = await h.BookingRequests.CreateBookingRequestAsync(
            Enquiry(h.UnitId, "buyer@example.com", cnic: "42101-7654321-1"), h.ClientUserId);

        var approved = await h.BookingRequests.ApproveBookingRequestAsync(request.Id, h.AdminUserId);

        // Deduplication did its job — no second CRM record was created for the same person...
        Assert.Equal(historic.Id, await ConvertedCustomerIdAsync(h, request.Id));
        Assert.Equal(1, await h.Db.Customers.CountAsync());

        // ...and did only its job. Two exact identifier matches, an authenticated submitter, and
        // the record is still owned by nobody. Portal access waits for an explicit claim.
        var customer = await h.Db.Customers.AsNoTracking().SingleAsync();
        Assert.Null(customer.UserId);
        Assert.Empty(await h.Db.CustomerAccountLinkAudits.ToListAsync());
    }

    [Fact]
    public async Task Approving_an_anonymous_enquiry_links_nobody()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        // Submitted while signed out — the public form allows it, and there is simply no
        // authenticated identity to carry forward.
        var request = await h.BookingRequests.CreateBookingRequestAsync(
            Enquiry(h.UnitId, "client@dams.test"), userId: null);

        var approved = await h.BookingRequests.ApproveBookingRequestAsync(request.Id, h.AdminUserId);

        // The address matches the seeded client's login exactly, and it still grants nothing.
        var customerId = await ConvertedCustomerIdAsync(h, request.Id);
        var customer = await h.Db.Customers.AsNoTracking().SingleAsync(c => c.Id == customerId);
        Assert.Null(customer.UserId);
    }

    [Fact]
    public async Task An_unverified_submitter_gets_a_booking_but_no_portal_access()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        // Standing in for a legacy account: a client login DAMS has never proven the address of.
        var client = await h.Db.Users.FirstAsync(u => u.UserId == h.ClientUserId);
        client.EmailVerifiedAt = null;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var request = await h.BookingRequests.CreateBookingRequestAsync(
            Enquiry(h.UnitId, "buyer@example.com"), h.ClientUserId);

        var approved = await h.BookingRequests.ApproveBookingRequestAsync(request.Id, h.AdminUserId);

        // The business workflow is untouched — the customer and booking exist, which is what the
        // sales team needs. Only the ownership grant is withheld, and the refusal is recorded.
        Assert.Equal(BookingRequestStatus.Approved, approved.Status);
        var customerId = await ConvertedCustomerIdAsync(h, request.Id);
        var customer = await h.Db.Customers.AsNoTracking().SingleAsync(c => c.Id == customerId);
        Assert.Null(customer.UserId);

        var audit = await h.Db.CustomerAccountLinkAudits.AsNoTracking().SingleAsync();
        Assert.Equal(CustomerAccountLinkAction.RejectedNotEligible, audit.Action);
        Assert.Equal(h.ClientUserId, audit.AttemptedUserId);
    }

    [Fact]
    public async Task An_enquiry_cannot_name_a_different_owner_than_its_submitter()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        // Everything the enquirer controls points at the admin's identity. None of it is read:
        // the only thing that decides ownership is the user id the controller took from the token
        // and handed to the service.
        var request = await h.BookingRequests.CreateBookingRequestAsync(
            Enquiry(h.UnitId, "admin@dams.test"), h.ClientUserId);

        var stored = await h.Db.BookingRequests.AsNoTracking().SingleAsync(br => br.Id == request.Id);
        Assert.Equal(h.ClientUserId, stored.UserId);

        var approved = await h.BookingRequests.ApproveBookingRequestAsync(request.Id, h.AdminUserId);
        var customerId = await ConvertedCustomerIdAsync(h, request.Id);
        var customer = await h.Db.Customers.AsNoTracking().SingleAsync(c => c.Id == customerId);

        Assert.Equal(h.ClientUserId, customer.UserId);
        Assert.NotEqual(h.AdminUserId, customer.UserId);
    }

    /// <summary>
    /// The customer the approval actually converted into, read from the stored request rather than
    /// found by matching anything — which is the same discipline the production code follows.
    /// </summary>
    private static async Task<int> ConvertedCustomerIdAsync(LeadTestHarness h, int requestId)
    {
        var customerId = await h.Db.BookingRequests.AsNoTracking()
            .Where(br => br.Id == requestId)
            .Select(br => br.CustomerId)
            .SingleAsync();

        Assert.NotNull(customerId);
        return customerId!.Value;
    }

    private static CreateBookingRequestDto Enquiry(
        int unitId, string email, string cnic = "42101-1111111-1") => new()
    {
        UnitId = unitId,
        FullName = "Website Enquirer",
        Phone = "03001234567",
        Email = email,
        CNIC = cnic,
        Address = "Karachi"
    };
}
