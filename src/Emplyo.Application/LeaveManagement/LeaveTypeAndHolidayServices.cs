using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Common.Models;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Application.LeaveManagement;

public class LeaveTypeService : ILeaveTypeService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly IDateTimeProvider _clock;

    public LeaveTypeService(IApplicationDbContext db, IAuditService audit, IDateTimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<LeaveTypeDto>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _db.LeaveTypes.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(t => t.IsActive);

        return await query.OrderBy(t => t.Name)
            .Select(t => new LeaveTypeDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                IsPaid = t.IsPaid,
                DefaultAnnualEntitlement = t.DefaultAnnualEntitlement,
                RequiresApproval = t.RequiresApproval,
                IsActive = t.IsActive,
                RequestCount = t.LeaveRequests.Count
            })
            .ToListAsync(ct);
    }

    public async Task<LeaveTypeFormDto?> GetFormAsync(int id, CancellationToken ct = default)
    {
        return await _db.LeaveTypes.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new LeaveTypeFormDto
            {
                Name = t.Name,
                Description = t.Description,
                IsPaid = t.IsPaid,
                DefaultAnnualEntitlement = t.DefaultAnnualEntitlement,
                RequiresApproval = t.RequiresApproval,
                IsActive = t.IsActive
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> CreateAsync(LeaveTypeFormDto dto, CancellationToken ct = default)
    {
        if (await _db.LeaveTypes.AnyAsync(t => t.Name == dto.Name.Trim(), ct))
            throw new ConflictException($"Leave type '{dto.Name}' already exists.");

        var type = new LeaveType
        {
            Name = dto.Name.Trim(),
            Description = dto.Description,
            IsPaid = dto.IsPaid,
            DefaultAnnualEntitlement = dto.DefaultAnnualEntitlement,
            RequiresApproval = dto.RequiresApproval,
            IsActive = dto.IsActive,
            CreatedAtUtc = _clock.UtcNow,
            UpdatedAtUtc = _clock.UtcNow
        };
        _db.LeaveTypes.Add(type);
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(new AuditEntry { Action = "LeaveType.Created", TargetType = nameof(LeaveType), TargetId = type.Id.ToString() }, ct);
        return type.Id;
    }

    public async Task UpdateAsync(int id, LeaveTypeFormDto dto, CancellationToken ct = default)
    {
        var type = await _db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Leave type not found.");
        if (await _db.LeaveTypes.AnyAsync(t => t.Name == dto.Name.Trim() && t.Id != id, ct))
            throw new ConflictException($"Leave type '{dto.Name}' already exists.");

        type.Name = dto.Name.Trim();
        type.Description = dto.Description;
        type.IsPaid = dto.IsPaid;
        type.DefaultAnnualEntitlement = dto.DefaultAnnualEntitlement;
        type.RequiresApproval = dto.RequiresApproval;
        type.IsActive = dto.IsActive;
        type.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(new AuditEntry { Action = "LeaveType.Updated", TargetType = nameof(LeaveType), TargetId = id.ToString() }, ct);
    }

    public async Task<IReadOnlyList<LeaveType>> GetActiveEntitiesAsync(CancellationToken ct = default)
        => await _db.LeaveTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync(ct);
}

public class HolidayService : IHolidayService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly IDateTimeProvider _clock;

    public HolidayService(IApplicationDbContext db, IAuditService audit, IDateTimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<HolidayDto>> GetAsync(int? year = null, CancellationToken ct = default)
    {
        var query = _db.Holidays.AsNoTracking().AsQueryable();
        if (year.HasValue)
            query = query.Where(h => h.Date.Year == year || h.IsRecurringAnnually);

        return await query.OrderBy(h => h.Date)
            .Select(h => new HolidayDto
            {
                Id = h.Id, Name = h.Name, Date = h.Date,
                Description = h.Description, IsRecurringAnnually = h.IsRecurringAnnually
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<HolidayDto>> GetUpcomingAsync(int count, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var holidays = await _db.Holidays.AsNoTracking().ToListAsync(ct);

        return holidays
            .Select(h => new HolidayDto
            {
                Id = h.Id,
                Name = h.Name,
                Date = NextOccurrence(h, today),
                Description = h.Description,
                IsRecurringAnnually = h.IsRecurringAnnually
            })
            .Where(h => h.Date >= today)
            .OrderBy(h => h.Date).ThenBy(h => h.Name)
            .Take(count)
            .ToList();
    }

    private static DateOnly NextOccurrence(Holiday holiday, DateOnly today)
    {
        if (!holiday.IsRecurringAnnually) return holiday.Date;

        var occurrence = new DateOnly(
            today.Year,
            holiday.Date.Month,
            Math.Min(holiday.Date.Day, DateTime.DaysInMonth(today.Year, holiday.Date.Month)));
        if (occurrence < today)
        {
            occurrence = new DateOnly(
                today.Year + 1,
                holiday.Date.Month,
                Math.Min(holiday.Date.Day, DateTime.DaysInMonth(today.Year + 1, holiday.Date.Month)));
        }
        return occurrence;
    }

    public async Task<HolidayFormDto?> GetFormAsync(int id, CancellationToken ct = default)
        => await _db.Holidays.AsNoTracking().Where(h => h.Id == id)
            .Select(h => new HolidayFormDto
            {
                Name = h.Name, Date = h.Date, Description = h.Description, IsRecurringAnnually = h.IsRecurringAnnually
            }).FirstOrDefaultAsync(ct);

    public async Task<int> CreateAsync(HolidayFormDto dto, CancellationToken ct = default)
    {
        if (await _db.Holidays.AnyAsync(h => h.Date == dto.Date && h.Name == dto.Name, ct))
            throw new ConflictException("A holiday with the same name and date already exists.");

        var holiday = new Holiday
        {
            Name = dto.Name.Trim(),
            Date = dto.Date,
            Description = dto.Description,
            IsRecurringAnnually = dto.IsRecurringAnnually,
            CreatedAtUtc = _clock.UtcNow,
            UpdatedAtUtc = _clock.UtcNow
        };
        _db.Holidays.Add(holiday);
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(new AuditEntry { Action = "Holiday.Created", TargetType = nameof(Holiday), TargetId = holiday.Id.ToString() }, ct);
        return holiday.Id;
    }

    public async Task UpdateAsync(int id, HolidayFormDto dto, CancellationToken ct = default)
    {
        var holiday = await _db.Holidays.FirstOrDefaultAsync(h => h.Id == id, ct)
            ?? throw new NotFoundException("Holiday not found.");
        holiday.Name = dto.Name.Trim();
        holiday.Date = dto.Date;
        holiday.Description = dto.Description;
        holiday.IsRecurringAnnually = dto.IsRecurringAnnually;
        holiday.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(new AuditEntry { Action = "Holiday.Updated", TargetType = nameof(Holiday), TargetId = id.ToString() }, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var holiday = await _db.Holidays.FirstOrDefaultAsync(h => h.Id == id, ct)
            ?? throw new NotFoundException("Holiday not found.");
        _db.Holidays.Remove(holiday);
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(new AuditEntry { Action = "Holiday.Deleted", TargetType = nameof(Holiday), TargetId = id.ToString() }, ct);
    }
}
