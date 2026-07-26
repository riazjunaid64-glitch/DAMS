using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class LeadConversionTests
{
    [Fact]
    public async Task ConvertingCreatesACustomerAndBooking_PreservingSourceAndOwner()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        await h.Leads.ChangeStageAsync(leadId, new ChangeLeadStageDto { Stage = LeadStage.Qualified }, h.Sales);

        var result = await h.Leads.ConvertAsync(leadId, new ConvertLeadDto
        {
            UnitId = h.UnitId,
            CNIC = "35202-1234567-1"
        }, h.Admin);

        Assert.True(result.Created);
        Assert.True(result.CustomerWasCreated);
        Assert.StartsWith("BK-", result.BookingReference);

        var booking = await h.Db.Bookings.AsNoTracking().FirstAsync(b => b.Id == result.BookingId);
        // The lead came in as a walk-in, and the booking records exactly that.
        Assert.Equal(CustomerSource.WalkIn, booking.Source);
        Assert.Equal(result.CustomerId, booking.CustomerId);
        Assert.Equal(h.SalesUserId, booking.AssignedSalesUserId);

        var customer = await h.Db.Customers.AsNoTracking().FirstAsync(c => c.Id == result.CustomerId);
        Assert.Equal(CustomerSource.WalkIn, customer.Source);
        Assert.Equal("35202-1234567-1", customer.CNIC);

        var lead = await h.LoadLeadAsync(leadId);
        Assert.Equal(LeadStage.Won, lead.Stage);
        Assert.Equal(result.CustomerId, lead.ConvertedCustomerId);
        Assert.Equal(result.BookingId, lead.ConvertedBookingId);
        Assert.Equal(h.AdminUserId, lead.ConvertedByUserId);
        Assert.NotNull(lead.ConvertedAt);
    }

    [Fact]
    public async Task ConversionHistorySurvives_AndTheLeadStaysReadable()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        await h.Documents.UploadAsync(leadId, LeadTestHarness.Pdf(), LeadDocumentCategory.Quotation, null, null, h.Sales);
        await h.SiteVisits.ScheduleAsync(leadId, new ScheduleSiteVisitDto
        {
            ScheduledAt = DateTime.UtcNow.AddDays(1),
            MeetingLocation = "Site office"
        }, h.Sales);

        var before = (await h.TimelineAsync(leadId)).Count;
        var result = await h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin);

        var timeline = await h.Leads.GetTimelineAsync(leadId, h.Admin);
        Assert.True(timeline.Count > before);
        Assert.Contains(timeline, a => a.Type == LeadActivityType.LeadConverted && a.BookingId == result.BookingId);
        Assert.Contains(timeline, a => a.Type == LeadActivityType.BookingCreated);
        Assert.Contains(timeline, a => a.Type == LeadActivityType.CustomerLinked);
        // Everything recorded before conversion is still there.
        Assert.Contains(timeline, a => a.Type == LeadActivityType.LeadCreated);
        Assert.Contains(timeline, a => a.Type == LeadActivityType.DocumentUploaded);

        Assert.Single(await h.Documents.GetForLeadAsync(leadId, h.Admin));
        Assert.Single(await h.Communications.GetForLeadAsync(leadId, h.Admin));
        Assert.Single(await h.SiteVisits.GetForLeadAsync(leadId, h.Admin));
    }

    [Fact]
    public async Task ConversionIsIdempotent()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        var first = await h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin);
        var second = await h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.BookingId, second.BookingId);
        Assert.Equal(first.CustomerId, second.CustomerId);
        Assert.Equal(1, await h.Db.Bookings.CountAsync());
        Assert.Equal(1, await h.Db.Customers.CountAsync());
    }

    [Fact]
    public async Task AnExistingCustomerIsReusedRatherThanDuplicated()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        h.Db.Customers.Add(new Customer
        {
            FullName = "Bilal Khan",
            Phone = "3001234567",
            Email = "lead3001234567@example.com"
        });
        await h.Db.SaveChangesAsync();

        var leadId = await h.CreateWorkedLeadAsync();
        var result = await h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin);

        Assert.False(result.CustomerWasCreated);
        Assert.Equal(1, await h.Db.Customers.CountAsync());
    }

    [Fact]
    public async Task AnExplicitlyChosenCustomerIsLinked()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var customer = new Customer { FullName = "Existing Buyer", Phone = "03337776666" };
        h.Db.Customers.Add(customer);
        await h.Db.SaveChangesAsync();

        var leadId = await h.CreateWorkedLeadAsync();
        var result = await h.Leads.ConvertAsync(leadId,
            new ConvertLeadDto { UnitId = h.UnitId, CustomerId = customer.Id }, h.Admin);

        Assert.Equal(customer.Id, result.CustomerId);
        Assert.False(result.CustomerWasCreated);
        Assert.Equal(1, await h.Db.Customers.CountAsync());
    }

    [Fact]
    public async Task ABlockedCustomerCannotBeConvertedInto()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var customer = new Customer { FullName = "Blocked Buyer", Phone = "03337776666", Status = CustomerStatus.Blocked };
        h.Db.Customers.Add(customer);
        await h.Db.SaveChangesAsync();

        var leadId = await h.CreateWorkedLeadAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Leads.ConvertAsync(leadId,
            new ConvertLeadDto { UnitId = h.UnitId, CustomerId = customer.Id }, h.Admin));

        Assert.Equal(LeadStage.Contacted, (await h.LoadLeadAsync(leadId)).Stage);
    }

    [Fact]
    public async Task AFailedConversionLeavesNoBookingAndNoWonLead()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        // Take the unit off the market so booking creation must fail.
        var unit = await h.Db.Units.FirstAsync(u => u.Id == h.UnitId);
        unit.Status = UnitStatus.Sold;
        await h.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin));

        h.Db.ChangeTracker.Clear();
        Assert.Equal(0, await h.Db.Bookings.CountAsync());

        var lead = await h.LoadLeadAsync(leadId);
        Assert.Equal(LeadStage.Contacted, lead.Stage);
        Assert.Null(lead.ConvertedBookingId);
        Assert.Null(lead.ConvertedAt);
    }

    [Fact]
    public async Task TwoLeadsCannotBookTheSameUnit()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var first = await h.CreateWorkedLeadAsync();
        var second = await h.CreateWorkedLeadAsync("03219998888");

        await h.Leads.ConvertAsync(first, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ConvertAsync(second, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin));

        Assert.Equal(1, await h.Db.Bookings.CountAsync());
    }

    [Fact]
    public async Task EmployeesCannotConvert()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Sales));

        Assert.Equal(0, await h.Db.Bookings.CountAsync());
    }

    [Fact]
    public async Task AClosedLeadMustBeReopenedBeforeConversion()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        var reasonId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "delayed_decision");
        await h.Leads.CloseAsync(leadId, dormant: true, new CloseLeadDto { ClosureReasonId = reasonId }, h.Sales);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin));
        Assert.Contains("Reopen", error.Message);

        await h.Leads.ReopenAsync(leadId, new ReopenLeadDto { Reason = "Came back." }, h.Admin);
        var result = await h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin);
        Assert.True(result.Created);
    }

    [Fact]
    public async Task AConvertedLeadIsFrozenAsHistory()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        await h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin);

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Leads.UpdateAsync(leadId, new UpdateLeadDto
        {
            FirstName = "Changed", Phone = "03001234567"
        }, h.Admin));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.AssignAsync(leadId, new AssignLeadDto { EmployeeId = h.OtherSalesEmployeeId, Reason = "x" }, h.Admin));

        var reasonId = await LeadIntakeAndDuplicateTests.ReasonIdAsync(h, "not_interested");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.CloseAsync(leadId, dormant: false, new CloseLeadDto { ClosureReasonId = reasonId }, h.Admin));

        // Reading it still works — that is the whole point of keeping it.
        Assert.NotNull(await h.Leads.GetByIdAsync(leadId, h.Admin));
    }

    [Fact]
    public async Task ConversionClosesOutstandingWorkAndTellsEveryone()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();
        await h.FollowUps.CreateAsync(leadId, new CreateLeadFollowUpDto
        {
            Title = "Chase documents",
            DueAt = DateTime.UtcNow.AddDays(1)
        }, h.Sales);

        await h.Leads.ConvertAsync(leadId, new ConvertLeadDto { UnitId = h.UnitId }, h.Admin);

        Assert.False(await h.Db.LeadFollowUps.AnyAsync(f => f.LeadId == leadId && f.Status == LeadFollowUpStatus.Pending));
        Assert.True(await h.Db.Notifications.AnyAsync(
            n => n.EntityType == NotificationEntityType.Lead && n.EntityId == leadId && n.Type == NotificationType.LeadConverted));
    }

    [Fact]
    public async Task NegotiatedTermsCarryIntoTheBooking()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var leadId = await h.CreateWorkedLeadAsync();

        var result = await h.Leads.ConvertAsync(leadId, new ConvertLeadDto
        {
            UnitId = h.UnitId,
            AgreedSalePrice = 9_500_000m,
            DiscountPercent = 5m,
            DiscountReason = "Launch offer",
            BookingAmountRequired = 500_000m
        }, h.Admin);

        var booking = await h.Db.Bookings.AsNoTracking().FirstAsync(b => b.Id == result.BookingId);
        Assert.Equal(9_500_000m, booking.AgreedSalePrice);
        Assert.Equal(475_000m, booking.DiscountAmount);
        Assert.Equal(500_000m, booking.BookingAmountRequired);
        // The lead reference is carried onto the booking for attribution reporting.
        Assert.StartsWith("LD-", booking.ReferenceId);
    }
}
