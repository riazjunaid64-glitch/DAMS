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

        var unit = new Unit
        {
            ProjectId = dto.ProjectId,
            UnitNumber = dto.UnitNumber,
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

        unit.UnitNumber = dto.UnitNumber;
        unit.UnitType = dto.UnitType;
        unit.FloorNumber = dto.FloorNumber;
        unit.Size = dto.Size;
        unit.Price = dto.Price;

        if (!Enum.TryParse<UnitStatus>(dto.Status, true, out var parsedStatus))
            throw new Exception("Invalid unit status provided.");

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

        _context.Units.Remove(unit);
        await _context.SaveChangesAsync();

        return true;
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
