using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingRequestDtos;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// An enquiry whose phone matches one open lead and whose email matches another cannot be
/// assumed to belong to either. Neither lead may change until someone who can see both decides.
/// </summary>
public sealed class LeadIdentityConflictTests
{
    private const string PhoneOfA = "0300-1234567";
    private const string EmailOfB = "b@example.com";

    private static async Task<(int A, int B)> TwoPeopleAsync(LeadTestHarness h)
    {
        var a = await h.Leads.IngestAsync(
            LeadTestHarness.Intake(firstName: "Person A", phone: PhoneOfA, email: "a@example.com"), h.Admin);
        var b = await h.Leads.IngestAsync(
            LeadTestHarness.Intake(firstName: "Person B", phone: "0321-7654321", email: EmailOfB), h.Admin);
        return (a.Lead!.Id, b.Lead!.Id);
    }

    private static LeadIntakeDto Conflicting(string? externalId = null) => new()
    {
        FirstName = "Who Is This",
        Phone = PhoneOfA,
        Email = EmailOfB,
        SourceCode = externalId == null ? "walk_in" : "facebook",
        Notes = "Conflicting enquiry.",
        ExternalProvider = externalId == null ? null : "meta",
        ExternalLeadId = externalId,
        AllowDuplicate = true
    };

    private static async Task AssertUntouchedAsync(LeadTestHarness h, int a, int b)
    {
        var notes = await h.Db.Leads.AsNoTracking()
            .Where(l => l.Id == a || l.Id == b)
            .Select(l => (l.Notes ?? "") + (l.SourceDetails ?? ""))
            .ToListAsync();
        Assert.All(notes, n => Assert.DoesNotContain("Conflicting enquiry.", n));
        Assert.Equal(2, await h.Db.Leads.CountAsync());
        Assert.Empty(await h.Db.LeadActivities.Where(x => x.Type == LeadActivityType.LeadEnriched).ToListAsync());
    }

    // ── Manual entry: the person is there, so they choose ─────────────────────────

    [Fact]
    public async Task ManualEntry_MatchingTwoLeads_ReportsBoth_AndChangesNeither()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var (a, b) = await TwoPeopleAsync(h);

        var result = await h.Leads.IngestAsync(Conflicting(), h.Admin);

        Assert.True(result.IdentityConflict);
        Assert.False(result.HeldForReview);
        Assert.Null(result.Lead);
        Assert.Equal(new[] { a, b }.OrderBy(x => x), result.ConflictingMatches.Select(m => m.LeadId!.Value).OrderBy(x => x));
        Assert.Equal("phone", result.ConflictingMatches.Single(m => m.LeadId == a).MatchedOn);
        Assert.Equal("email", result.ConflictingMatches.Single(m => m.LeadId == b).MatchedOn);
        Assert.Empty(await h.Db.LeadIntakeHolds.ToListAsync());
        await AssertUntouchedAsync(h, a, b);
    }

    [Fact]
    public async Task ManualEntry_ChoosingOneOfTheConflictingLeads_AddsToThatLeadOnly()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var (a, b) = await TwoPeopleAsync(h);

        var choice = Conflicting();
        choice.ExpectedExistingLeadId = b;
        var result = await h.Leads.IngestAsync(choice, h.Admin);

        Assert.True(result.EnrichedExisting);
        Assert.Equal(b, result.Lead!.Id);
        Assert.Contains("Conflicting enquiry.", result.Lead.Notes);
        var notesOfA = await h.Db.Leads.Where(l => l.Id == a).Select(l => l.Notes).SingleAsync();
        Assert.DoesNotContain("Conflicting enquiry.", notesOfA ?? "");
    }

    [Fact]
    public async Task ManualEntry_ChoosingALeadOutsideTheConflict_WritesNothing()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var (a, b) = await TwoPeopleAsync(h);

        var choice = Conflicting();
        choice.ExpectedExistingLeadId = a + b + 100;
        var result = await h.Leads.IngestAsync(choice, h.Admin);

        Assert.True(result.IdentityConflict);
        Assert.Null(result.Lead);
        await AssertUntouchedAsync(h, a, b);
    }

    [Fact]
    public async Task ManualEntry_ByStaffWhoCannotSeeEveryCandidate_RevealsNothing()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var (a, b) = await TwoPeopleAsync(h);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Leads.IngestAsync(Conflicting(), h.Sales));

        Assert.DoesNotContain("LD-", ex.Message);
        await AssertUntouchedAsync(h, a, b);
    }

    // ── External channels: nobody is there, so the enquiry is held ───────────────

    [Fact]
    public async Task ExternalEnquiry_MatchingTwoLeads_IsHeld_AndChangesNeither()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var (a, b) = await TwoPeopleAsync(h);

        var result = await h.Leads.IngestAsync(Conflicting("meta-conflict-1"), actor: null, trustedExternal: true);

        Assert.True(result.HeldForReview);
        Assert.NotNull(result.HoldId);
        Assert.Null(result.Lead);
        // The result can go back to an external caller: it must not name internal leads.
        Assert.Null(result.Match);
        Assert.Empty(result.ConflictingMatches);
        await AssertUntouchedAsync(h, a, b);
        Assert.Empty(await h.Db.LeadExternalSubmissions.Where(s => s.ExternalLeadId == "meta-conflict-1").ToListAsync());

        var hold = await h.Db.LeadIntakeHolds.SingleAsync();
        Assert.Equal(LeadIntakeHoldStatus.Open, hold.Status);
        Assert.Contains("Conflicting enquiry.", hold.PayloadJson);
    }

    [Fact]
    public async Task ReplayingAHeldEnquiry_FindsTheSameHold()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        await TwoPeopleAsync(h);

        var first = await h.Leads.IngestAsync(Conflicting("meta-conflict-1"), actor: null, trustedExternal: true);
        var replay = await h.Leads.IngestAsync(Conflicting("meta-conflict-1"), actor: null, trustedExternal: true);

        Assert.True(replay.AlreadyIngested);
        Assert.True(replay.HeldForReview);
        Assert.Equal(first.HoldId, replay.HoldId);
        Assert.Single(await h.Db.LeadIntakeHolds.ToListAsync());
    }

    [Fact]
    public async Task SingleMatchAndNoMatch_ExternalIntake_IsUnchanged()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var (a, _) = await TwoPeopleAsync(h);

        var single = new LeadIntakeDto
        {
            FirstName = "A Again", Phone = PhoneOfA, SourceCode = "facebook",
            ExternalProvider = "meta", ExternalLeadId = "meta-single", AllowDuplicate = true
        };
        var enriched = await h.Leads.IngestAsync(single, actor: null, trustedExternal: true);
        Assert.True(enriched.EnrichedExisting);
        Assert.Equal(a, enriched.Lead!.Id);

        var fresh = new LeadIntakeDto
        {
            FirstName = "Someone New", Phone = "0333-1112223", SourceCode = "facebook",
            ExternalProvider = "meta", ExternalLeadId = "meta-new", AllowDuplicate = true
        };
        var created = await h.Leads.IngestAsync(fresh, actor: null, trustedExternal: true);
        Assert.False(created.IsDuplicate);
        Assert.NotNull(created.Lead);

        Assert.Empty(await h.Db.LeadIntakeHolds.ToListAsync());
    }

    // ── Review ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnAdministrator_SeesTheHeldEnquiry_WithBothLeads()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var (a, b) = await TwoPeopleAsync(h);
        await h.Leads.IngestAsync(Conflicting("meta-conflict-1"), actor: null, trustedExternal: true);

        var list = await h.Leads.GetIntakeHoldsAsync(h.Admin);
        Assert.Equal(1, list.TotalWaiting);
        var held = Assert.Single(list.Items);

        Assert.Equal("Who Is This", held.FirstName);
        Assert.Equal(EmailOfB, held.Email);
        Assert.Equal("Facebook", held.SourceName);
        var candidateA = held.Candidates.Single(c => c.LeadId == a);
        var candidateB = held.Candidates.Single(c => c.LeadId == b);
        Assert.Equal(("phone", "Person A Khan"), (candidateA.MatchedOn, candidateA.LeadName));
        Assert.Equal(("email", "Person B Khan"), (candidateB.MatchedOn, candidateB.LeadName));
        Assert.True(candidateA.IsOpen && candidateB.IsOpen);
    }

    [Fact]
    public async Task OnlyAnAdministrator_CanReviewOrResolve()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var (a, _) = await TwoPeopleAsync(h);
        var held = await h.Leads.IngestAsync(Conflicting("meta-conflict-1"), actor: null, trustedExternal: true);

        await Assert.ThrowsAsync<LeadAuthorizationException>(() => h.Leads.GetIntakeHoldsAsync(h.Manager));
        await Assert.ThrowsAsync<LeadAuthorizationException>(() =>
            h.Leads.ResolveIntakeHoldAsync(held.HoldId!.Value, new ResolveLeadIntakeHoldDto { LeadId = a }, h.Manager));
    }

    [Fact]
    public async Task Resolving_AddsTheEnquiryToTheChosenLead_AndAReplayThenFindsIt()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var (a, b) = await TwoPeopleAsync(h);
        var held = await h.Leads.IngestAsync(Conflicting("meta-conflict-1"), actor: null, trustedExternal: true);

        var lead = await h.Leads.ResolveIntakeHoldAsync(
            held.HoldId!.Value, new ResolveLeadIntakeHoldDto { LeadId = a, Notes = "Same person, new email." }, h.Admin);

        Assert.Equal(a, lead!.Id);
        Assert.Contains("Conflicting enquiry.", lead.Notes);
        var notesOfB = await h.Db.Leads.Where(l => l.Id == b).Select(l => l.Notes).SingleAsync();
        Assert.DoesNotContain("Conflicting enquiry.", notesOfB ?? "");

        var hold = await h.Db.LeadIntakeHolds.AsNoTracking().SingleAsync();
        Assert.Equal(LeadIntakeHoldStatus.Resolved, hold.Status);
        Assert.Equal(a, hold.ResolvedLeadId);
        Assert.Equal(h.AdminUserId, hold.ResolvedByUserId);
        Assert.Equal(a, (await h.Db.LeadExternalSubmissions.SingleAsync(s => s.ExternalLeadId == "meta-conflict-1")).LeadId);
        Assert.Empty((await h.Leads.GetIntakeHoldsAsync(h.Admin)).Items);

        var replay = await h.Leads.IngestAsync(Conflicting("meta-conflict-1"), actor: null, trustedExternal: true);
        Assert.True(replay.AlreadyIngested);
        Assert.Equal(a, replay.Lead!.Id);
    }

    [Fact]
    public async Task Resolving_IsRefused_ForALeadItDidNotMatch_AClosedLead_OrASecondTime()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var (a, b) = await TwoPeopleAsync(h);
        var other = await h.Leads.IngestAsync(
            LeadTestHarness.Intake(firstName: "Unrelated", phone: "0345-5556667", email: "c@example.com"), h.Admin);
        var holdId = (await h.Leads.IngestAsync(Conflicting("meta-conflict-1"), actor: null, trustedExternal: true)).HoldId!.Value;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ResolveIntakeHoldAsync(holdId, new ResolveLeadIntakeHoldDto { LeadId = other.Lead!.Id }, h.Admin));

        var storedB = await h.Db.Leads.SingleAsync(l => l.Id == b);
        storedB.Stage = LeadStage.Lost;
        await h.Db.SaveChangesAsync();
        var closed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ResolveIntakeHoldAsync(holdId, new ResolveLeadIntakeHoldDto { LeadId = b }, h.Admin));
        Assert.Contains("closed", closed.Message);
        Assert.False((await h.Leads.GetIntakeHoldsAsync(h.Admin)).Items.Single().Candidates.Single(c => c.LeadId == b).IsOpen);

        await h.Leads.ResolveIntakeHoldAsync(holdId, new ResolveLeadIntakeHoldDto { LeadId = a }, h.Admin);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ResolveIntakeHoldAsync(holdId, new ResolveLeadIntakeHoldDto { LeadId = a }, h.Admin));
    }

    [Fact]
    public async Task Dismissing_TouchesNoLead_AndAReplayStaysDismissed()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var (a, b) = await TwoPeopleAsync(h);
        var holdId = (await h.Leads.IngestAsync(Conflicting("meta-conflict-1"), actor: null, trustedExternal: true)).HoldId!.Value;

        var none = await h.Leads.ResolveIntakeHoldAsync(holdId, new ResolveLeadIntakeHoldDto { Dismiss = true, Notes = "Spam." }, h.Admin);

        Assert.Null(none);
        Assert.Equal(LeadIntakeHoldStatus.Dismissed, (await h.Db.LeadIntakeHolds.AsNoTracking().SingleAsync()).Status);
        await AssertUntouchedAsync(h, a, b);

        var replay = await h.Leads.IngestAsync(Conflicting("meta-conflict-1"), actor: null, trustedExternal: true);
        Assert.Null(replay.Lead);
        Assert.False(replay.HeldForReview);
        Assert.Single(await h.Db.LeadIntakeHolds.ToListAsync());
    }

    [Fact]
    public async Task Resolving_NeedsExactlyOneOfALeadOrDismiss()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var (a, _) = await TwoPeopleAsync(h);
        var holdId = (await h.Leads.IngestAsync(Conflicting("meta-conflict-1"), actor: null, trustedExternal: true)).HoldId!.Value;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ResolveIntakeHoldAsync(holdId, new ResolveLeadIntakeHoldDto(), h.Admin));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ResolveIntakeHoldAsync(holdId, new ResolveLeadIntakeHoldDto { LeadId = a, Dismiss = true }, h.Admin));
    }

    // ── Website booking requests ────────────────────────────────────────────────

    [Fact]
    public async Task AWebsiteRequest_MatchingTwoLeads_IsSaved_ThenLinkedWhenResolved()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var (a, b) = await TwoPeopleAsync(h);

        var request = await h.BookingRequests.CreateBookingRequestAsync(new CreateBookingRequestDto
        {
            UnitId = h.UnitId, FullName = "Website Enquirer", Phone = "03001234567",
            Email = EmailOfB, CNIC = "42101-1111111-1", Address = "Karachi"
        }, h.ClientUserId);

        var stored = await h.Db.BookingRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id);
        Assert.Null(stored.LeadId);
        var hold = await h.Db.LeadIntakeHolds.AsNoTracking().SingleAsync();
        Assert.Equal(request.Id, hold.BookingRequestId);
        Assert.Equal(2, await h.Db.Leads.CountAsync());

        // Approval cannot go ahead on a guess, and the hold cannot be thrown away.
        var approval = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.BookingRequests.ApproveBookingRequestAsync(request.Id, h.AdminUserId));
        Assert.Contains("Held enquiries", approval.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Leads.ResolveIntakeHoldAsync(hold.Id, new ResolveLeadIntakeHoldDto { Dismiss = true }, h.Admin));

        await h.Leads.ResolveIntakeHoldAsync(hold.Id, new ResolveLeadIntakeHoldDto { LeadId = b }, h.Admin);

        Assert.Equal(b, (await h.Db.BookingRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id)).LeadId);
        var notesOfA = await h.Db.Leads.Where(l => l.Id == a).Select(l => l.SourceDetails).SingleAsync();
        Assert.DoesNotContain("Website booking request", notesOfA ?? "");
    }

    private static async Task<int> HeldWebsiteRequestAsync(LeadTestHarness h) =>
        (await h.BookingRequests.CreateBookingRequestAsync(new CreateBookingRequestDto
        {
            UnitId = h.UnitId, FullName = "Website Enquirer", Phone = "03001234567",
            Email = EmailOfB, CNIC = "42101-1111111-1", Address = "Karachi"
        }, h.ClientUserId)).Id;

    [Fact]
    public async Task Backfill_LeavesAHeldWebsiteRequestForTheAdministrator()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var (_, b) = await TwoPeopleAsync(h);
        var requestId = await HeldWebsiteRequestAsync(h);

        var backfill = await h.Leads.BackfillFromBookingRequestsAsync(h.Admin);

        // A third lead for the same person would also make resolving the hold record the same
        // website submission twice.
        Assert.Equal(0, backfill.LeadsCreated);
        Assert.Equal(1, backfill.HeldForReview);
        Assert.Equal(2, await h.Db.Leads.CountAsync());
        Assert.Null((await h.Db.BookingRequests.AsNoTracking().SingleAsync(r => r.Id == requestId)).LeadId);

        var holdId = (await h.Db.LeadIntakeHolds.AsNoTracking().SingleAsync()).Id;
        await h.Leads.ResolveIntakeHoldAsync(holdId, new ResolveLeadIntakeHoldDto { LeadId = b }, h.Admin);
        Assert.Equal(b, (await h.Db.BookingRequests.AsNoTracking().SingleAsync(r => r.Id == requestId)).LeadId);
    }

    [Fact]
    public async Task ApprovingAnOlderRequestWithConflictingDetails_HoldsIt_ThenApprovesOnceResolved()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var (a, _) = await TwoPeopleAsync(h);
        // Submitted before lead management: no lead, and no hold was ever made for it.
        var legacy = new Domain.Entities.BookingRequest
        {
            UnitId = h.UnitId, UserId = h.ClientUserId, FullName = "Older Enquirer", Phone = "03001234567",
            Email = EmailOfB, CNIC = "42101-1111111-1", Address = "Karachi", Status = BookingRequestStatus.Pending
        };
        h.Db.BookingRequests.Add(legacy);
        await h.Db.SaveChangesAsync();

        var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.BookingRequests.ApproveBookingRequestAsync(legacy.Id, h.AdminUserId));
        Assert.Contains("Held enquiries", blocked.Message);

        // The refusal left somewhere to resolve it: a hold that survived the failed approval.
        var hold = await h.Db.LeadIntakeHolds.AsNoTracking().SingleAsync();
        Assert.Equal(legacy.Id, hold.BookingRequestId);
        Assert.Equal(2, await h.Db.Leads.CountAsync());

        await h.Leads.ResolveIntakeHoldAsync(hold.Id, new ResolveLeadIntakeHoldDto { LeadId = a }, h.Admin);
        var approved = await h.BookingRequests.ApproveBookingRequestAsync(legacy.Id, h.AdminUserId);

        Assert.Equal(BookingRequestStatus.Approved, approved.Status);
        Assert.Equal(a, (await h.Db.BookingRequests.AsNoTracking().SingleAsync(r => r.Id == legacy.Id)).LeadId);
    }

    [Fact]
    public async Task RejectingOrCancellingAHeldRequest_ClosesItsHold()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        await TwoPeopleAsync(h);
        var rejected = await HeldWebsiteRequestAsync(h);

        await h.BookingRequests.RejectBookingRequestAsync(rejected, h.AdminUserId, "Unit no longer available.");

        var hold = await h.Db.LeadIntakeHolds.AsNoTracking().SingleAsync(x => x.BookingRequestId == rejected);
        Assert.Equal(LeadIntakeHoldStatus.Dismissed, hold.Status);
        Assert.Equal(h.AdminUserId, hold.ResolvedByUserId);
        Assert.Contains("rejected", hold.ResolutionNotes);

        var cancelled = (await h.BookingRequests.CreateBookingRequestAsync(new CreateBookingRequestDto
        {
            UnitId = h.SecondUnitId, FullName = "Website Enquirer", Phone = "03001234567",
            Email = EmailOfB, CNIC = "42101-1111111-1", Address = "Karachi"
        }, h.ClientUserId)).Id;
        await h.BookingRequests.CancelBookingRequestAsync(cancelled, h.ClientUserId);

        var cancelledHold = await h.Db.LeadIntakeHolds.AsNoTracking().SingleAsync(x => x.BookingRequestId == cancelled);
        Assert.Equal(LeadIntakeHoldStatus.Dismissed, cancelledHold.Status);
        // Nobody on staff decided it, so no staff member is recorded as having resolved it.
        Assert.Null(cancelledHold.ResolvedByUserId);
        Assert.Empty((await h.Leads.GetIntakeHoldsAsync(h.Admin)).Items);
    }

    [Fact]
    public async Task AHoldWhoseRequestIsNoLongerPending_CanBeDismissed()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        await TwoPeopleAsync(h);
        var requestId = await HeldWebsiteRequestAsync(h);
        // Data from before rejecting closed the hold: the request was rejected, the hold left open.
        var request = await h.Db.BookingRequests.SingleAsync(r => r.Id == requestId);
        request.Status = BookingRequestStatus.Rejected;
        await h.Db.SaveChangesAsync();
        var holdId = (await h.Db.LeadIntakeHolds.AsNoTracking().SingleAsync()).Id;

        await h.Leads.ResolveIntakeHoldAsync(holdId, new ResolveLeadIntakeHoldDto { Dismiss = true }, h.Admin);

        Assert.Equal(LeadIntakeHoldStatus.Dismissed, (await h.Db.LeadIntakeHolds.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task AWebhookRetryWithoutAnId_DoesNotQueueTheSameEnquiryTwice()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        await TwoPeopleAsync(h);
        LeadIntakeDto NoId() => new()
        {
            FirstName = "Who Is This", Phone = PhoneOfA, Email = EmailOfB, SourceCode = "website",
            ExternalProvider = "portal", Notes = "Same body, retried.", AllowDuplicate = true
        };

        var first = await h.Leads.IngestAsync(NoId(), actor: null, trustedExternal: true);
        var retry = await h.Leads.IngestAsync(NoId(), actor: null, trustedExternal: true);

        Assert.True(first.HeldForReview && retry.HeldForReview);
        Assert.Equal(first.HoldId, retry.HoldId);
        Assert.Single(await h.Db.LeadIntakeHolds.ToListAsync());
    }
}
