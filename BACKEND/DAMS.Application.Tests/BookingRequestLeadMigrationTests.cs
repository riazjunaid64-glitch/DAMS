using DAMS.Application.DTOs.BookingRequestDtos;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// The website booking-request flow after it became a lead source: the request record and
/// its endpoints keep working exactly as before, but the sales workflow now lives on a Lead.
/// </summary>
public sealed class BookingRequestLeadMigrationTests
{
    [Fact]
    public async Task AWebsiteRequestCreatesALeadWithTheWebsiteSourceAndItsOriginalDetails()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var request = await h.BookingRequests.CreateBookingRequestAsync(Request(h), h.ClientUserId);

        Assert.Equal(BookingRequestStatus.Pending, request.Status);
        Assert.NotNull(request.LeadId);

        var lead = await h.LoadLeadAsync(request.LeadId!.Value);
        Assert.Equal(LeadStage.New, lead.Stage);
        Assert.Equal(LeadAssignmentState.Unassigned, lead.AssignmentState);
        Assert.Equal("Aiman", lead.FirstName);
        Assert.Equal("Raza", lead.LastName);
        Assert.Equal(h.UnitId, lead.InterestedUnitId);
        Assert.Equal(h.ProjectId, lead.InterestedProjectId);
        Assert.Equal(request.RequestedAt, lead.ExternalSubmittedAt);

        var source = await h.Db.LeadSources.AsNoTracking().FirstAsync(s => s.Id == lead.LeadSourceId);
        Assert.Equal("website", source.Code);
        Assert.Contains($"#{request.Id}", lead.SourceDetails);
    }

    [Fact]
    public async Task AWebsiteRequestNoLongerTakesTheUnitOffTheMarket()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        await h.BookingRequests.CreateBookingRequestAsync(Request(h), h.ClientUserId);

        var unit = await h.Db.Units.AsNoTracking().FirstAsync(u => u.Id == h.UnitId);
        Assert.Equal(UnitStatus.Available, unit.Status);
    }

    [Fact]
    public async Task SeveralPeopleMayEnquireAboutTheSameUnit()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var first = await h.BookingRequests.CreateBookingRequestAsync(Request(h), h.ClientUserId);
        var second = await h.BookingRequests.CreateBookingRequestAsync(
            Request(h, "Nadia Iqbal", "03337776666", "nadia@example.com"), null);

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.LeadId, second.LeadId);
        Assert.Equal(2, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task ARepeatEnquiryFromTheSamePersonJoinsTheirExistingLead()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var first = await h.BookingRequests.CreateBookingRequestAsync(Request(h), h.ClientUserId);
        var second = await h.BookingRequests.CreateBookingRequestAsync(
            RequestForUnit(h, h.SecondUnitId), h.ClientUserId);

        Assert.Equal(first.LeadId, second.LeadId);
        Assert.Equal(1, await h.Db.Leads.CountAsync());
        Assert.Equal(2, await h.Db.BookingRequests.CountAsync());

        var timeline = await h.TimelineAsync(first.LeadId!.Value);
        Assert.Contains(timeline, a => a.Type == LeadActivityType.LeadEnriched);
    }

    [Fact]
    public async Task RejectingOneOfSeveralActiveEnquiriesKeepsTheSharedLeadAndItsOpenWorkActive()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var first = await h.BookingRequests.CreateBookingRequestAsync(Request(h), h.ClientUserId);
        var second = await h.BookingRequests.CreateBookingRequestAsync(
            RequestForUnit(h, h.SecondUnitId), h.ClientUserId);
        var leadId = first.LeadId!.Value;

        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);
        var followUp = await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            AssignedEmployeeId = h.SalesEmployeeId,
            Title = "Discuss second enquiry",
            DueAt = DateTime.UtcNow.AddDays(2)
        }, h.Admin);
        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            AssignedEmployeeId = h.SalesEmployeeId,
            ProjectId = h.ProjectId,
            UnitId = h.SecondUnitId,
            ScheduledAt = DateTime.UtcNow.AddDays(3),
            MeetingLocation = "Floria Heights sales office"
        }, h.Admin);

        var rejected = await h.BookingRequests.RejectBookingRequestAsync(first.Id, h.AdminUserId, "Unit unavailable.");

        Assert.Equal(BookingRequestStatus.Rejected, rejected.Status);
        Assert.Equal(BookingRequestStatus.Pending,
            await h.Db.BookingRequests.Where(br => br.Id == second.Id).Select(br => br.Status).SingleAsync());
        Assert.Equal(LeadStage.SiteVisitScheduled, (await h.LoadLeadAsync(leadId)).Stage);
        Assert.Equal(LeadFollowUpStatus.Pending,
            await h.Db.LeadFollowUps.Where(f => f.Id == followUp.Id).Select(f => f.Status).SingleAsync());
        Assert.Equal(LeadSiteVisitStatus.Scheduled,
            await h.Db.LeadSiteVisits.Where(v => v.Id == visit.Id).Select(v => v.Status).SingleAsync());
    }

    [Fact]
    public async Task CancellingOneOfSeveralActiveEnquiriesKeepsTheSharedLeadAndItsOpenWorkActive()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var first = await h.BookingRequests.CreateBookingRequestAsync(Request(h), h.ClientUserId);
        var second = await h.BookingRequests.CreateBookingRequestAsync(
            RequestForUnit(h, h.SecondUnitId), h.ClientUserId);
        var leadId = first.LeadId!.Value;

        await h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.SalesEmployeeId }, h.Admin);
        var followUp = await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            AssignedEmployeeId = h.SalesEmployeeId,
            Title = "Discuss second enquiry",
            DueAt = DateTime.UtcNow.AddDays(2)
        }, h.Admin);
        var visit = await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            AssignedEmployeeId = h.SalesEmployeeId,
            ProjectId = h.ProjectId,
            UnitId = h.SecondUnitId,
            ScheduledAt = DateTime.UtcNow.AddDays(3),
            MeetingLocation = "Floria Heights sales office"
        }, h.Admin);

        var cancelled = await h.BookingRequests.CancelBookingRequestAsync(first.Id, h.ClientUserId);

        Assert.Equal(BookingRequestStatus.Cancelled, cancelled.Status);
        Assert.Equal(BookingRequestStatus.Pending,
            await h.Db.BookingRequests.Where(br => br.Id == second.Id).Select(br => br.Status).SingleAsync());
        Assert.Equal(LeadStage.SiteVisitScheduled, (await h.LoadLeadAsync(leadId)).Stage);
        Assert.Equal(LeadFollowUpStatus.Pending,
            await h.Db.LeadFollowUps.Where(f => f.Id == followUp.Id).Select(f => f.Status).SingleAsync());
        Assert.Equal(LeadSiteVisitStatus.Scheduled,
            await h.Db.LeadSiteVisits.Where(v => v.Id == visit.Id).Select(v => v.Status).SingleAsync());
    }

    [Fact]
    public async Task ApprovingARequestConvertsTheLeadIntoACustomerAndBooking()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var request = await h.BookingRequests.CreateBookingRequestAsync(Request(h), h.ClientUserId);

        var approved = await h.BookingRequests.ApproveBookingRequestAsync(request.Id, h.AdminUserId);

        Assert.Equal(BookingRequestStatus.Approved, approved.Status);
        Assert.Equal(h.AdminUserId, approved.ReviewedByUserId);
        var stored = await h.Db.BookingRequests.AsNoTracking().FirstAsync(br => br.Id == approved.Id);
        Assert.NotNull(stored.CustomerId);

        var booking = await h.Db.Bookings.AsNoTracking().SingleAsync();
        Assert.Equal(CustomerSource.Website, booking.Source);
        Assert.Equal(request.Id, booking.BookingRequestId);
        Assert.Equal(BookingStatus.AwaitingBookingAmount, booking.Status);

        var lead = await h.LoadLeadAsync(request.LeadId!.Value);
        Assert.Equal(LeadStage.Won, lead.Stage);
        Assert.Equal(booking.Id, lead.ConvertedBookingId);

        var customer = await h.Db.Customers.AsNoTracking().SingleAsync();
        Assert.Equal(CustomerSource.Website, customer.Source);
        Assert.Equal("35202-9876543-2", customer.CNIC);

        var unit = await h.Db.Units.AsNoTracking().FirstAsync(u => u.Id == h.UnitId);
        Assert.Equal(UnitStatus.Reserved, unit.Status);
    }

    [Fact]
    public async Task ApprovalCannotHappenTwice()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var request = await h.BookingRequests.CreateBookingRequestAsync(Request(h), h.ClientUserId);
        await h.BookingRequests.ApproveBookingRequestAsync(request.Id, h.AdminUserId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.BookingRequests.ApproveBookingRequestAsync(request.Id, h.AdminUserId));

        Assert.Equal(1, await h.Db.Bookings.CountAsync());
    }

    [Fact]
    public async Task RejectingARequestClosesItsLeadAsLostWithTheReason()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var request = await h.BookingRequests.CreateBookingRequestAsync(Request(h), h.ClientUserId);

        var rejected = await h.BookingRequests.RejectBookingRequestAsync(request.Id, h.AdminUserId, "Unit already reserved.");

        Assert.Equal(BookingRequestStatus.Rejected, rejected.Status);
        var lead = await h.LoadLeadAsync(request.LeadId!.Value);
        Assert.Equal(LeadStage.Lost, lead.Stage);
        Assert.NotNull(lead.ClosureReasonId);
        Assert.Equal("Unit already reserved.", lead.ClosureNotes);
        Assert.Contains(await h.TimelineAsync(lead.Id), a => a.Type == LeadActivityType.LeadLost);
    }

    [Fact]
    public async Task WithdrawingARequestMakesItsLeadDormantRatherThanLost()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var request = await h.BookingRequests.CreateBookingRequestAsync(Request(h), h.ClientUserId);

        var cancelled = await h.BookingRequests.CancelBookingRequestAsync(request.Id, h.ClientUserId);

        Assert.Equal(BookingRequestStatus.Cancelled, cancelled.Status);
        var lead = await h.LoadLeadAsync(request.LeadId!.Value);
        Assert.Equal(LeadStage.Dormant, lead.Stage);
        // The full history is kept so the lead can be picked up again later.
        Assert.Contains(await h.TimelineAsync(lead.Id), a => a.Type == LeadActivityType.LeadCreated);
    }

    [Fact]
    public async Task OnlyTheirOwnPendingRequestCanBeWithdrawn()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var request = await h.BookingRequests.CreateBookingRequestAsync(Request(h), h.ClientUserId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.BookingRequests.CancelBookingRequestAsync(request.Id, h.SalesUserId));

        await h.BookingRequests.CancelBookingRequestAsync(request.Id, h.ClientUserId);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.BookingRequests.CancelBookingRequestAsync(request.Id, h.ClientUserId));
    }

    [Fact]
    public async Task ExistingListingAndStatsEndpointsStillWork()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var pending = await h.BookingRequests.CreateBookingRequestAsync(Request(h), h.ClientUserId);
        var other = await h.BookingRequests.CreateBookingRequestAsync(
            Request(h, "Nadia Iqbal", "03337776666", "nadia@example.com"), null);
        await h.BookingRequests.RejectBookingRequestAsync(other.Id, h.AdminUserId, "Not eligible.");

        var list = await h.BookingRequests.GetBookingRequestsAsync(new BookingRequestFilterDto());
        Assert.Equal(2, list.TotalCount);
        Assert.All(list.Items, i => Assert.Equal("Floria Heights", i.ProjectName));

        var mine = await h.BookingRequests.GetMyBookingRequestsAsync(h.ClientUserId);
        Assert.Single(mine);
        Assert.Equal(pending.Id, mine[0].Id);

        var filtered = await h.BookingRequests.GetBookingRequestsAsync(
            new BookingRequestFilterDto { Status = BookingRequestStatus.Rejected });
        Assert.Single(filtered.Items);

        var searched = await h.BookingRequests.GetBookingRequestsAsync(
            new BookingRequestFilterDto { SearchTerm = "nadia" });
        Assert.Single(searched.Items);

        var stats = await h.BookingRequests.GetBookingRequestStatsAsync();
        Assert.Equal(1, stats["pending"]);
        Assert.Equal(1, stats["rejected"]);
        Assert.Equal(2, stats["total"]);

        Assert.True(await h.BookingRequests.HasPendingRequestForUnitAsync(h.UnitId));
    }

    [Fact]
    public async Task TheSamePersonCannotSubmitTwoPendingRequestsForOneUnit()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        await h.BookingRequests.CreateBookingRequestAsync(Request(h), h.ClientUserId);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.BookingRequests.CreateBookingRequestAsync(Request(h), h.ClientUserId));

        Assert.Contains("already have a pending request", error.Message);
        Assert.Equal(1, await h.Db.BookingRequests.CountAsync());
    }

    [Fact]
    public async Task EnquiriesAreRefusedForUnitsThatAreNoLongerForSale()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var unit = await h.Db.Units.FirstAsync(u => u.Id == h.UnitId);
        unit.Status = UnitStatus.Sold;
        await h.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.BookingRequests.CreateBookingRequestAsync(Request(h), h.ClientUserId));
    }

    // ── Backfill ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BackfillCreatesLeadsForHistoricalRequests_AndIsRepeatable()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var pending = LegacyRequest(h, "Old Pending", "03005550001", BookingRequestStatus.Pending);
        var rejected = LegacyRequest(h, "Old Rejected", "03005550002", BookingRequestStatus.Rejected);
        rejected.RejectionReason = "Documents incomplete.";
        var cancelled = LegacyRequest(h, "Old Cancelled", "03005550003", BookingRequestStatus.Cancelled);
        h.Db.BookingRequests.AddRange(pending, rejected, cancelled);
        await h.Db.SaveChangesAsync();

        var result = await h.Leads.BackfillFromBookingRequestsAsync(h.Admin);

        Assert.Equal(3, result.BookingRequestsScanned);
        Assert.Equal(3, result.LeadsCreated);
        Assert.Equal(3, await h.Db.Leads.CountAsync());

        h.Db.ChangeTracker.Clear();
        var leads = await h.Db.BookingRequests.AsNoTracking()
            .Include(br => br.Lead)
            .OrderBy(br => br.Id)
            .ToListAsync();

        Assert.All(leads, br => Assert.NotNull(br.LeadId));
        Assert.Equal(LeadStage.New, leads[0].Lead!.Stage);
        Assert.Equal(LeadStage.Lost, leads[1].Lead!.Stage);
        Assert.Equal("Documents incomplete.", leads[1].Lead!.ClosureNotes);
        Assert.Equal(LeadStage.Dormant, leads[2].Lead!.Stage);
        // The original submission time is what the lead is dated from.
        Assert.Equal(pending.RequestedAt, leads[0].Lead!.CreatedAt);

        // Running it again is a no-op — nothing is duplicated.
        var second = await h.Leads.BackfillFromBookingRequestsAsync(h.Admin);
        Assert.Equal(0, second.LeadsCreated);
        Assert.Equal(3, second.AlreadyLinked);
        Assert.Equal(3, await h.Db.Leads.CountAsync());
    }

    [Fact]
    public async Task BackfillMarksAnAlreadyApprovedRequestAsWonAndLinksItsBooking()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        var customer = new Customer { FullName = "Historic Buyer", Phone = "03005550009" };
        h.Db.Customers.Add(customer);
        await h.Db.SaveChangesAsync();

        var request = LegacyRequest(h, "Historic Buyer", "03005550009", BookingRequestStatus.Approved);
        request.CustomerId = customer.Id;
        request.ReviewedAt = DateTime.UtcNow.AddDays(-20);
        request.ReviewedByUserId = h.AdminUserId;
        h.Db.BookingRequests.Add(request);
        await h.Db.SaveChangesAsync();

        var booking = new Booking
        {
            BookingReference = "BK-000001",
            CustomerId = customer.Id,
            UnitId = h.UnitId,
            BookingRequestId = request.Id,
            Source = CustomerSource.Website,
            ListPrice = 10_000_000m,
            AgreedSalePrice = 10_000_000m
        };
        h.Db.Bookings.Add(booking);
        await h.Db.SaveChangesAsync();

        await h.Leads.BackfillFromBookingRequestsAsync(h.Admin);

        h.Db.ChangeTracker.Clear();
        var lead = await h.Db.Leads.AsNoTracking().SingleAsync();
        Assert.Equal(LeadStage.Won, lead.Stage);
        Assert.Equal(customer.Id, lead.ConvertedCustomerId);
        Assert.Equal(booking.Id, lead.ConvertedBookingId);
        Assert.Equal(request.ReviewedAt, lead.ConvertedAt);
    }

    [Fact]
    public async Task BackfillIsAdminOnly()
    {
        await using var h = await LeadTestHarness.CreateAsync();

        await Assert.ThrowsAsync<Common.LeadAuthorizationException>(
            () => h.Leads.BackfillFromBookingRequestsAsync(h.Manager));
    }

    [Fact]
    public async Task ALegacyRequestWithoutALeadCanStillBeApproved()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var request = LegacyRequest(h, "Old Pending", "03005550001", BookingRequestStatus.Pending);
        h.Db.BookingRequests.Add(request);
        await h.Db.SaveChangesAsync();

        var approved = await h.BookingRequests.ApproveBookingRequestAsync(request.Id, h.AdminUserId);

        Assert.Equal(BookingRequestStatus.Approved, approved.Status);
        Assert.NotNull(approved.LeadId);
        var lead = await h.LoadLeadAsync(approved.LeadId!.Value);
        Assert.Equal(LeadStage.Won, lead.Stage);
    }

    // ── Builders ────────────────────────────────────────────────────────────────

    private static CreateBookingRequestDto Request(
        LeadTestHarness h,
        string fullName = "Aiman Raza",
        string phone = "03001234567",
        string email = "aiman@example.com") =>
        new()
        {
            UnitId = h.UnitId,
            FullName = fullName,
            Phone = phone,
            Email = email,
            CNIC = "35202-9876543-2",
            Address = "12 Model Town, Lahore",
            Notes = "Prefer a corner unit."
        };

    private static CreateBookingRequestDto RequestForUnit(LeadTestHarness h, int unitId)
    {
        var dto = Request(h);
        dto.UnitId = unitId;
        return dto;
    }

    private static BookingRequest LegacyRequest(LeadTestHarness h, string name, string phone, BookingRequestStatus status) =>
        new()
        {
            UnitId = h.UnitId,
            FullName = name,
            Phone = phone,
            Email = $"{phone}@example.com",
            CNIC = "35202-0000000-0",
            Address = "Old address",
            Status = status,
            RequestedAt = DateTime.UtcNow.AddDays(-30)
        };
}
