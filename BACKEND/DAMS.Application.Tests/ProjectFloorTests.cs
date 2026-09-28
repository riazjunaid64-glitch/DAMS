using System.Text;
using DAMS.Application.Common;
using DAMS.Application.DTOs.ProjectDtos;
using DAMS.Application.DTOs.UnitDtos;
using DAMS.Application.Services;
using DAMS.Application.Services.Notifications;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// KAN-57: every project has its own floor list. A unit's floor is the list entry with the same
/// number; a floor that has units keeps its number, and the name is what every screen and the
/// receipt show.
/// </summary>
public class ProjectFloorTests
{
    [Fact]
    public async Task SavingTheFloorListTrimsSortsAndCountsUnits()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var projects = Projects(h);

        var saved = await projects.ReplaceFloorsAsync(h.ProjectId,
        [
            Floor(2, "2nd floor"),
            Floor(0, "  Ground floor "),
            Floor(-1, "Parking"),
            Floor(1, "1st floor"),
        ]);

        Assert.Equal([-1, 0, 1, 2], saved.Select(f => f.Number));
        Assert.Equal("Ground floor", saved[1].Name);
        // The harness puts both of its units on floor 1.
        Assert.Equal([0, 0, 2, 0], saved.Select(f => f.UnitCount));

        var read = await projects.GetFloorsAsync(h.ProjectId);
        Assert.Equal(saved.Select(f => (f.Number, f.Name, f.UnitCount)), read!.Select(f => (f.Number, f.Name, f.UnitCount)));

        var detail = await projects.GetProjectByIdAsync(h.ProjectId);
        Assert.Equal(4, detail!.FloorCount);
        Assert.Equal(["Parking", "Ground floor", "1st floor", "2nd floor"], detail.Floors!.Select(f => f.Name));

        var listed = Assert.Single(await projects.GetAllProjectsAsync(), p => p.Id == h.ProjectId);
        Assert.Equal(4, listed.FloorCount);
        Assert.Null(listed.Floors);

        Assert.Null(await projects.GetFloorsAsync(h.ProjectId + 100_000));
    }

    [Fact]
    public async Task SavingAgainReplacesTheWholeList_EvenWhenNamesMoveBetweenFloors()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var projects = Projects(h);
        await projects.ReplaceFloorsAsync(h.ProjectId, [Floor(0, "Ground floor"), Floor(1, "1st floor"), Floor(2, "Rooftop")]);
        h.Db.ChangeTracker.Clear();

        var saved = await projects.ReplaceFloorsAsync(h.ProjectId, [Floor(1, "1st floor"), Floor(2, "2nd floor"), Floor(3, "Rooftop")]);

        Assert.Equal([(1, "1st floor"), (2, "2nd floor"), (3, "Rooftop")], saved.Select(f => (f.Number, f.Name)));
        Assert.Equal(3, await h.Db.ProjectFloors.CountAsync(f => f.ProjectId == h.ProjectId));
    }

    [Fact]
    public async Task ABadRowSavesNothing()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var projects = Projects(h);
        await projects.ReplaceFloorsAsync(h.ProjectId, [Floor(1, "1st floor")]);
        h.Db.ChangeTracker.Clear();

        await AssertRejected(projects, h.ProjectId, "Floor number 1 is used twice.", Floor(1, "1st floor"), Floor(1, "Mezzanine"));
        await AssertRejected(projects, h.ProjectId, "\"parking\" is used for two floors.", Floor(1, "1st floor"), Floor(-1, "Parking"), Floor(-2, "parking "));
        await AssertRejected(projects, h.ProjectId, "Every floor needs a name.", Floor(1, "1st floor"), Floor(2, "   "));
        await AssertRejected(projects, h.ProjectId, "Add at least one floor.");
        await AssertRejected(projects, h.ProjectId, "Floor numbers go from -10 to 200.", Floor(1, "1st floor"), Floor(-11, "Deep"));
        await AssertRejected(projects, h.ProjectId, "A floor name can be at most 40 characters.", Floor(1, new string('x', 41)));

        var tooMany = Enumerable.Range(-10, 202).Select(n => Floor(n, $"Level {n}")).ToArray();
        await AssertRejected(projects, h.ProjectId, "A project can have at most 200 floors.", tooMany);

        var stored = await h.Db.ProjectFloors.AsNoTracking().Where(f => f.ProjectId == h.ProjectId).ToListAsync();
        Assert.Equal("1st floor", Assert.Single(stored).Name);
    }

    [Fact]
    public async Task AFloorWithUnitsCanBeRenamedButNotRemovedOrRenumbered()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var projects = Projects(h);
        await projects.ReplaceFloorsAsync(h.ProjectId, [Floor(0, "Ground floor"), Floor(1, "1st floor")]);
        h.Db.ChangeTracker.Clear();

        const string locked = "1st floor has 2 units, so its number can't change. Move the units first.";
        await AssertRejected(projects, h.ProjectId, locked, Floor(0, "Ground floor"));
        await AssertRejected(projects, h.ProjectId, locked, Floor(0, "Ground floor"), Floor(6, "1st floor"));

        var renamed = await projects.ReplaceFloorsAsync(h.ProjectId, [Floor(0, "Lobby"), Floor(1, "Podium")]);
        Assert.Equal("Podium", renamed.Single(f => f.Number == 1).Name);

        h.Db.ChangeTracker.Clear();
        var unit = await new UnitService(h.Db).GetUnitByIdAsync(h.UnitId);
        Assert.Equal("Podium", unit!.FloorName);
    }

    [Fact]
    public async Task TheFirstListMustAlreadyHoldEveryFloorThatHasUnits()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var one = await h.Db.Units.SingleAsync(u => u.Id == h.UnitId);
        one.FloorNumber = -1;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await AssertRejected(Projects(h), h.ProjectId,
            "Basement 1 has 1 unit, so its number can't change. Move the units first.",
            Floor(0, "Ground floor"), Floor(1, "1st floor"));
    }

    [Fact]
    public async Task AUnitMustSitOnAFloorFromTheListOnceTheProjectHasOne()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var units = new UnitService(h.Db);

        // No list yet: any floor, as before.
        var anywhere = await units.CreateUnitAsync(NewUnit(h.ProjectId, "B-101", 37));
        Assert.Equal("37th floor", anywhere.FloorName);
        h.Db.ChangeTracker.Clear();

        await Projects(h).ReplaceFloorsAsync(h.ProjectId, [Floor(-1, "Parking"), Floor(1, "1st floor"), Floor(37, "Rooftop")]);
        h.Db.ChangeTracker.Clear();

        var offList = await Assert.ThrowsAsync<BusinessRuleException>(() => units.CreateUnitAsync(NewUnit(h.ProjectId, "B-102", 5)));
        Assert.Equal("Pick a floor from this project's floor list.", offList.Message);

        var parking = await units.CreateUnitAsync(NewUnit(h.ProjectId, "P-01", -1));
        Assert.Equal("Parking", parking.FloorName);
        h.Db.ChangeTracker.Clear();

        var moveOff = await Assert.ThrowsAsync<BusinessRuleException>(() => units.UpdateUnitAsync(h.UnitId, Edit("A-101", 5)));
        Assert.Equal("Pick a floor from this project's floor list.", moveOff.Message);
        h.Db.ChangeTracker.Clear();

        var moved = await units.UpdateUnitAsync(h.UnitId, Edit("A-101", 37));
        Assert.Equal("Rooftop", moved.FloorName);
    }

    [Fact]
    public async Task AUnitAlreadyOffTheListCanStillBeSavedOnItsOwnFloor()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        await Projects(h).ReplaceFloorsAsync(h.ProjectId, [Floor(1, "1st floor"), Floor(2, "2nd floor")]);
        // Older data: a unit whose floor the list does not have.
        var stray = await h.Db.Units.SingleAsync(u => u.Id == h.UnitId);
        stray.FloorNumber = 9;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var saved = await new UnitService(h.Db).UpdateUnitAsync(h.UnitId, Edit("A-101", 9, 12_000_000m));

        Assert.Equal(12_000_000m, saved.Price);
        Assert.Equal("9th floor", saved.FloorName);
    }

    [Fact]
    public async Task UnitsShowTheProjectFloorName_OrTheStandardLabelWithoutAList()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var units = new UnitService(h.Db);

        Assert.All(await units.GetUnitsByProjectIdAsync(h.ProjectId), u => Assert.Equal("1st floor", u.FloorName));

        await Projects(h).ReplaceFloorsAsync(h.ProjectId, [Floor(0, "Ground floor"), Floor(1, "Podium")]);
        h.Db.ChangeTracker.Clear();

        Assert.All(await units.GetUnitsByProjectIdAsync(h.ProjectId), u => Assert.Equal("Podium", u.FloorName));
        Assert.Equal("Podium", (await units.GetUnitByIdAsync(h.SecondUnitId))!.FloorName);
    }

    [Fact]
    public void TheStandardLabelMatchesTheWebApp()
    {
        Assert.Equal("Basement 2", ProjectFloorNames.Standard(-2));
        Assert.Equal("Ground floor", ProjectFloorNames.Standard(0));
        Assert.Equal("1st floor", ProjectFloorNames.Standard(1));
        Assert.Equal("2nd floor", ProjectFloorNames.Standard(2));
        Assert.Equal("3rd floor", ProjectFloorNames.Standard(3));
        Assert.Equal("11th floor", ProjectFloorNames.Standard(11));
        Assert.Equal("12th floor", ProjectFloorNames.Standard(12));
        Assert.Equal("13th floor", ProjectFloorNames.Standard(13));
        Assert.Equal("21st floor", ProjectFloorNames.Standard(21));
        Assert.Equal("112th floor", ProjectFloorNames.Standard(112));
    }

    [Fact]
    public async Task TheReceiptAndTheBookingShowTheFloorName_GroundAndBasementIncluded()
    {
        await using var h = await NotificationTestHarness.CreateAsync();
        var paymentId = await h.RecordPaymentAsync();
        var unit = await h.Db.Units.SingleAsync(u => u.Id == h.UnitId);
        unit.FloorNumber = 0;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        // No list: the standard label, which the receipt used to leave out for ground and basements.
        var fallback = await h.Bookings.GetPaymentReceiptAsync(h.BookingId, paymentId);
        Assert.Equal("Ground floor", fallback.FloorName);
        Assert.Contains("(Ground floor)", PdfText(fallback));

        await new ProjectService(h.Db, new MemoryCache(new MemoryCacheOptions()))
            .ReplaceFloorsAsync(h.ProjectId, [Floor(-1, "Parking"), Floor(0, "Lobby level"), Floor(1, "1st floor")]);
        h.Db.ChangeTracker.Clear();

        var named = await h.Bookings.GetPaymentReceiptAsync(h.BookingId, paymentId);
        Assert.Equal("Lobby level", named.FloorName);
        Assert.Contains("(Lobby level)", PdfText(named));

        Assert.Equal("Lobby level", (await h.Bookings.GetBookingByIdAsync(h.BookingId))!.FloorName);
        var listed = await h.Bookings.GetBookingsAsync(new DTOs.BookingDtos.BookingFilterDto { Page = 1, PageSize = 20 });
        Assert.Equal("Lobby level", Assert.Single(listed.Items, b => b.Id == h.BookingId).FloorName);
    }

    private static ProjectService Projects(LeadTestHarness h) => new(h.Db, new MemoryCache(new MemoryCacheOptions()));

    private static ProjectFloorDto Floor(int number, string name) => new() { Number = number, Name = name };

    private static async Task AssertRejected(ProjectService projects, int projectId, string message, params ProjectFloorDto[] floors)
    {
        var rejected = await Assert.ThrowsAsync<BusinessRuleException>(() => projects.ReplaceFloorsAsync(projectId, floors.ToList()));
        Assert.Equal(message, rejected.Message);
    }

    private static string PdfText(DTOs.BookingDtos.PaymentReceiptDto receipt) =>
        Encoding.ASCII.GetString(ReceiptPdfWriter.Build(receipt, "DAMS Estates", null, null, null, "Rs"));

    private static CreateUnitDto NewUnit(int projectId, string number, int floor) => new()
    {
        ProjectId = projectId,
        UnitNumber = number,
        UnitType = "2 Bed",
        FloorNumber = floor,
        Size = 900m,
        Price = 9_000_000m
    };

    private static UpdateUnitDto Edit(string number, int floor, decimal price = 10_000_000m) => new()
    {
        UnitNumber = number,
        UnitType = "2 Bed",
        FloorNumber = floor,
        Size = 1200m,
        Price = price,
        Status = nameof(UnitStatus.Available)
    };
}
