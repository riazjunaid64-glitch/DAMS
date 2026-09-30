using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Application.DTOs.UnitDtos;
using DAMS.Infrastructure.Data;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;

namespace DAMS.Application.Services
{
    public class UnitService : IUnitService
{
    private readonly AppDbContext _context;

    public UnitService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<UnitResponseDto> CreateUnitAsync(CreateUnitDto dto)
    {
        var projectExists = await _context.Projects
            .AnyAsync(p => p.Id == dto.ProjectId);

        if (!projectExists)
            throw new MissingRecordException("Project not found");

        RequireMeasurable(dto.Size, dto.Price);
        var unitNumber = NormaliseUnitNumber(dto.UnitNumber);
        await EnsureUnitNumberIsFree(dto.ProjectId, unitNumber, null);
        var floorName = await RequireProjectFloor(dto.ProjectId, dto.FloorNumber, null);

        var unit = new Unit
        {
            ProjectId = dto.ProjectId,
            UnitNumber = unitNumber,
            UnitType = UnitTypes.Require(dto.UnitType),
            FloorNumber = dto.FloorNumber,
            Size = dto.Size,
            Price = dto.Price
        };

        _context.Units.Add(unit);
        await _context.SaveChangesAsync();

        return Map(unit, floorName);
    }

    public async Task<UnitResponseDto?> GetUnitByIdAsync(int id, bool includeLiveBooking = false)
    {
        var unit = await _context.Units
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UnitResponseDto
            {
                Id = u.Id,
                ProjectId = u.ProjectId,
                UnitNumber = u.UnitNumber,
                UnitType = u.UnitType,
                FloorNumber = u.FloorNumber,
                Size = u.Size,
                Price = u.Price,
                Status = u.Status.ToString()
            })
            .FirstOrDefaultAsync();

        if (unit != null)
            await NameFloors(unit.ProjectId, [unit]);

        if (unit != null && includeLiveBooking)
        {
            var live = await _context.Bookings.AsNoTracking()
                .Where(b => b.UnitId == id && b.Status != BookingStatus.Cancelled)
                .Select(b => new { b.Id, b.BookingReference })
                .FirstOrDefaultAsync();
            unit.LiveBookingId = live?.Id;
            unit.LiveBookingReference = live?.BookingReference;
        }

        return unit;
    }

    public async Task<List<UnitResponseDto>> GetUnitsByProjectIdAsync(int projectId)
    {
        var units = await _context.Units
            .AsNoTracking()
            .Where(u => u.ProjectId == projectId)
            .OrderBy(u => u.FloorNumber)
            .ThenBy(u => u.UnitNumber)
            .Select(u => new UnitResponseDto
            {
                Id = u.Id,
                ProjectId = u.ProjectId,
                UnitNumber = u.UnitNumber,
                UnitType = u.UnitType,
                FloorNumber = u.FloorNumber,
                Size = u.Size,
                Price = u.Price,
                Status = u.Status.ToString()
            })
            .ToListAsync();

        await NameFloors(projectId, units);
        return units;
    }

    public async Task<UnitResponseDto> UpdateUnitAsync(int id, UpdateUnitDto dto)
    {
        var unit = await _context.Units.FindAsync(id);

        if (unit == null)
            throw new MissingRecordException("Unit not found");

        RequireMeasurable(dto.Size, dto.Price);
        var unitNumber = NormaliseUnitNumber(dto.UnitNumber);
        await EnsureUnitNumberIsFree(unit.ProjectId, unitNumber, id);
        var floorName = await RequireProjectFloor(unit.ProjectId, dto.FloorNumber, unit.FloorNumber);

        unit.UnitNumber = unitNumber;
        unit.UnitType = UnitTypes.ResolveForUpdate(dto.UnitType, unit.UnitType);
        unit.FloorNumber = dto.FloorNumber;
        unit.Size = dto.Size;
        unit.Price = dto.Price;

        if (!Enum.TryParse<UnitStatus>(dto.Status, true, out var parsedStatus))
            throw new BusinessRuleException("Invalid unit status provided.");

        // While a unit has an active booking its status is workflow-managed; editing it
        // here (e.g. back to Available) would allow a second booking on the same unit.
        if (parsedStatus != unit.Status)
        {
            var hasActiveBooking = await _context.Bookings
                .AnyAsync(b => b.UnitId == id && b.Status != BookingStatus.Cancelled);

            if (hasActiveBooking)
                throw new BusinessRuleException(
                    "This unit has an active booking, so its status is managed by the booking workflow and cannot be changed here. Cancel or complete the booking instead.");
        }

        unit.Status = parsedStatus;
        unit.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Map(unit, floorName);
    }

    public async Task<bool> DeleteUnitAsync(int id)
    {
        var unit = await _context.Units.FindAsync(id);

        if (unit == null)
            throw new Exception("Unit not found");

        // The Booking->Unit FK is Restrict; without this check the delete surfaces as
        // an unhandled database error instead of a friendly message.
        var hasBookings = await _context.Bookings.AnyAsync(b => b.UnitId == id);
        if (hasBookings)
            throw new Exception("This unit has bookings and cannot be deleted.");

        _context.Units.Remove(unit);
        await _context.SaveChangesAsync();

        return true;
    }

    private static void RequireMeasurable(decimal size, decimal price)
    {
        if (size <= 0)
            throw new BusinessRuleException("Size must be greater than 0.");
        if (price <= 0)
            throw new BusinessRuleException("Price must be greater than 0.");
    }

    private static string NormaliseUnitNumber(string unitNumber)
    {
        var trimmed = (unitNumber ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            throw new BusinessRuleException("Unit number is required.");

        return trimmed;
    }

    // The unit number identifies the apartment within its project, so a second row carrying the
    // same number is a duplicate record of one apartment - and since every booking guard is per
    // UnitId, each duplicate can take its own active booking. The unique index behind this check
    // is what actually prevents it; this is here so the user is told why rather than shown a
    // database error.
    private async Task EnsureUnitNumberIsFree(int projectId, string unitNumber, int? exceptUnitId)
    {
        var taken = await _context.Units
            .AnyAsync(u => u.ProjectId == projectId
                && u.UnitNumber == unitNumber
                && (exceptUnitId == null || u.Id != exceptUnitId));

        if (taken)
            throw new BusinessRuleException($"Unit number \"{unitNumber}\" already exists in this project.");
    }

    // Once a project has a floor list, a unit must sit on one of its floors. With no list, any
    // number is accepted as before. On an edit the unit's current floor is always accepted, so a
    // unit on a floor that is not on the list (older data) can still have its other fields saved.
    // Returns the name the floor is shown under.
    private async Task<string> RequireProjectFloor(int projectId, int floorNumber, int? currentFloorNumber)
    {
        var floors = await _context.ProjectFloors
            .AsNoTracking()
            .Where(f => f.ProjectId == projectId)
            .Select(f => new { f.Number, f.Name })
            .ToListAsync();

        var floor = floors.FirstOrDefault(f => f.Number == floorNumber);
        if (floor != null)
            return floor.Name;

        if (floors.Count > 0 && floorNumber != currentFloorNumber)
            throw new BusinessRuleException("Pick a floor from this project's floor list.");

        return ProjectFloorNames.Standard(floorNumber);
    }

    private async Task NameFloors(int projectId, IReadOnlyCollection<UnitResponseDto> units)
    {
        if (units.Count == 0)
            return;

        var names = await ProjectFloorNames.LoadAsync(_context, [projectId]);
        foreach (var unit in units)
            unit.FloorName = names.For(unit.ProjectId, unit.FloorNumber);
    }

    private static UnitResponseDto Map(Unit unit, string floorName)
    {
        return new UnitResponseDto
        {
            Id = unit.Id,
            ProjectId = unit.ProjectId,
            UnitNumber = unit.UnitNumber,
            UnitType = unit.UnitType,
            FloorNumber = unit.FloorNumber,
            FloorName = floorName,
            Size = unit.Size,
            Price = unit.Price,
            Status = unit.Status.ToString()
        };
    }
}
}
