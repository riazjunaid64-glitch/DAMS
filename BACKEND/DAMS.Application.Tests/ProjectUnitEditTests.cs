using DAMS.Application.DTOs.ProjectDtos;
using DAMS.Application.DTOs.UnitDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// KAN-56: editing a unit never rewrites its status or a booking's agreed price, and a
/// duplicate unit number or project name comes back as the message the form shows.
/// </summary>
public class ProjectUnitEditTests
{
    [Fact]
    public async Task ADuplicateUnitNumberIsRejectedWithTheMessageTheFormShows()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var units = new UnitService(h.Db);

        var rejected = await Assert.ThrowsAsync<Exception>(() => units.UpdateUnitAsync(h.UnitId, Draft("A-102", 10_000_000m)));

        Assert.Equal("Unit number \"A-102\" already exists in this project.", rejected.Message);
    }

    [Fact]
    public async Task ChangingThePriceOfABookedUnitLeavesTheBookingAndTheStatusAlone()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        var unit = await h.Db.Units.SingleAsync(u => u.Id == h.UnitId);
        unit.Status = UnitStatus.Booked;
        h.Db.Bookings.Add(new Booking
        {
            UnitId = h.UnitId,
            CustomerId = 1,
            BookingReference = "BK-000810",
            ListPrice = 10_000_000m,
            AgreedSalePrice = 9_500_000m,
            Status = BookingStatus.AwaitingBookingAmount
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var updated = await new UnitService(h.Db).UpdateUnitAsync(
            h.UnitId, Draft("A-101", 12_000_000m, nameof(UnitStatus.Booked)));

        Assert.Equal(12_000_000m, updated.Price);
        Assert.Equal(nameof(UnitStatus.Booked), updated.Status);

        h.Db.ChangeTracker.Clear();
        var booking = await h.Db.Bookings.SingleAsync();
        Assert.Equal(10_000_000m, booking.ListPrice);
        Assert.Equal(9_500_000m, booking.AgreedSalePrice);
        Assert.Equal(UnitStatus.Booked, (await h.Db.Units.SingleAsync(u => u.Id == h.UnitId)).Status);

        var blocked = await Assert.ThrowsAsync<Exception>(() => new UnitService(h.Db).UpdateUnitAsync(
            h.UnitId, Draft("A-101", 12_000_000m, nameof(UnitStatus.Available))));
        Assert.Contains("managed by the booking workflow", blocked.Message);
    }

    [Fact]
    public async Task RenamingAProjectOntoAnotherNameIsRejected_KeepingItsOwnNameIsNot()
    {
        await using var h = await LeadTestHarness.CreateAsync();
        h.Db.Projects.Add(new Project { ProjectName = "Other Tower", Location = "Karachi", CreatedById = h.AdminUserId });
        await h.Db.SaveChangesAsync();

        var projects = new ProjectService(h.Db, new DiscardingCache());

        var rejected = await Assert.ThrowsAsync<Exception>(() => projects.UpdateProjectAsync(h.ProjectId, Project("Other Tower")));
        Assert.Equal("Project name already exists.", rejected.Message);

        var updated = await projects.UpdateProjectAsync(h.ProjectId, Project("Floria Heights", "Islamabad", ProjectStatus.Completed));
        Assert.Equal("Floria Heights", updated.ProjectName);
        Assert.Equal("Islamabad", updated.Location);
        Assert.Equal(ProjectStatus.Completed, updated.Status);
    }

    private static UpdateUnitDto Draft(string number, decimal price, string status = nameof(UnitStatus.Available)) => new()
    {
        UnitNumber = number,
        UnitType = "2 Bed",
        FloorNumber = 1,
        Size = 1200m,
        Price = price,
        Status = status
    };

    private static UpdateProjectDto Project(string name, string location = "Lahore", ProjectStatus status = ProjectStatus.Ongoing) => new()
    {
        ProjectName = name,
        Location = location,
        Status = status
    };

    private sealed class DiscardingCache : IMemoryCache
    {
        public void Dispose() { }
        public ICacheEntry CreateEntry(object key) => throw new NotSupportedException();
        public void Remove(object key) { }
        public bool TryGetValue(object key, out object? value)
        {
            value = null;
            return false;
        }
    }
}
