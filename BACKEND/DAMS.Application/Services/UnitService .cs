using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
            throw new Exception("Project not found");

        var unitNumber = NormaliseUnitNumber(dto.UnitNumber);
        await EnsureUnitNumberIsFree(dto.ProjectId, unitNumber, null);

        var unit = new Unit
        {
            ProjectId = dto.ProjectId,
            UnitNumber = unitNumber,
            UnitType = dto.UnitType,
            FloorNumber = dto.FloorNumber,
            Size = dto.Size,
            Price = dto.Price
        };

        _context.Units.Add(unit);
        await _context.SaveChangesAsync();

        return Map(unit);
    }

    public async Task<UnitResponseDto?> GetUnitByIdAsync(int id)
    {
        return await _context.Units
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
    }

    public async Task<List<UnitResponseDto>> GetUnitsByProjectIdAsync(int projectId)
    {
        return await _context.Units
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
    }

    public async Task<UnitResponseDto> UpdateUnitAsync(int id, UpdateUnitDto dto)
    {
        var unit = await _context.Units.FindAsync(id);

        if (unit == null)
            throw new Exception("Unit not found");

        var unitNumber = NormaliseUnitNumber(dto.UnitNumber);
        await EnsureUnitNumberIsFree(unit.ProjectId, unitNumber, id);

        unit.UnitNumber = unitNumber;
        unit.UnitType = dto.UnitType;
        unit.FloorNumber = dto.FloorNumber;
        unit.Size = dto.Size;
        unit.Price = dto.Price;

        if (!Enum.TryParse<UnitStatus>(dto.Status, true, out var parsedStatus))
            throw new Exception("Invalid unit status provided.");

        // While a unit has an active booking its status is workflow-managed; editing it
        // here (e.g. back to Available) would allow a second booking on the same unit.
        if (parsedStatus != unit.Status)
        {
            var hasActiveBooking = await _context.Bookings
                .AnyAsync(b => b.UnitId == id && b.Status != BookingStatus.Cancelled);

            if (hasActiveBooking)
                throw new Exception(
                    "This unit has an active booking, so its status is managed by the booking workflow and cannot be changed here. Cancel or complete the booking instead.");
        }

        unit.Status = parsedStatus;
        unit.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Map(unit);
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

    private static string NormaliseUnitNumber(string unitNumber)
    {
        var trimmed = (unitNumber ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            throw new Exception("Unit number is required.");

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
            throw new Exception($"Unit number \"{unitNumber}\" already exists in this project.");
    }

    private static UnitResponseDto Map(Unit unit)
    {
        return new UnitResponseDto
        {
            Id = unit.Id,
            ProjectId = unit.ProjectId,
            UnitNumber = unit.UnitNumber,
            UnitType = unit.UnitType,
            FloorNumber = unit.FloorNumber,
            Size = unit.Size,
            Price = unit.Price,
            Status = unit.Status.ToString()
        };
    }
}
}
