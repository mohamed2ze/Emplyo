using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Common.Models;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Enums;
using Emplyo.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Application.Attendance;

public class AttendanceService : IAttendanceService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly IAppSettingsService _settings;
    private readonly IDateTimeProvider _clock;

    public AttendanceService(IApplicationDbContext db, IAuditService audit,
        ICurrentUserService currentUser, IAppSettingsService settings, IDateTimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
        _settings = settings;
        _clock = clock;
    }

    public async Task<PagedResult<AttendanceListItemDto>> GetPagedAsync(AttendanceFilter filter, CancellationToken ct = default)
    {
        var (page, pageSize) = Paging.Normalize(filter.Page, filter.PageSize);
        var query = _db.AttendanceRecords.AsNoTracking()
            .Include(a => a.Employee).ThenInclude(e => e.Department)
            .AsQueryable();

        if (filter.EmployeeId.HasValue)
            query = query.Where(a => a.EmployeeId == filter.EmployeeId);
        if (filter.From.HasValue)
            query = query.Where(a => a.WorkDate >= filter.From);
        if (filter.To.HasValue)
            query = query.Where(a => a.WorkDate <= filter.To);
        if (filter.Status.HasValue)
            query = query.Where(a => a.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(a => a.Employee.FirstName.Contains(term)
                || a.Employee.LastName.Contains(term)
                || a.Employee.EmployeeNumber.Contains(term));
        }

        var desc = string.Equals(filter.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = (filter.SortBy?.ToLowerInvariant()) switch
        {
            "employee" => desc ? query.OrderByDescending(a => a.Employee.FirstName) : query.OrderBy(a => a.Employee.FirstName),
            "status" => desc ? query.OrderByDescending(a => a.Status) : query.OrderBy(a => a.Status),
            _ => desc ? query.OrderByDescending(a => a.WorkDate) : query.OrderBy(a => a.WorkDate)
        };

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AttendanceListItemDto
            {
                Id = a.Id,
                EmployeeId = a.EmployeeId,
                EmployeeName = a.Employee.FirstName + " " + a.Employee.LastName,
                EmployeeNumber = a.Employee.EmployeeNumber,
                DepartmentName = a.Employee.Department != null ? a.Employee.Department.Name : null,
                WorkDate = a.WorkDate,
                ClockInUtc = a.ClockInUtc,
                ClockOutUtc = a.ClockOutUtc,
                Status = a.Status,
                Notes = a.Notes
            })
            .ToListAsync(ct);

        return PagedResult<AttendanceListItemDto>.Create(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<AttendanceListItemDto>> GetForEmployeeAsync(int employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        return await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.WorkDate >= from && a.WorkDate <= to)
            .OrderByDescending(a => a.WorkDate)
            .Select(a => new AttendanceListItemDto
            {
                Id = a.Id,
                EmployeeId = a.EmployeeId,
                EmployeeName = a.Employee.FirstName + " " + a.Employee.LastName,
                EmployeeNumber = a.Employee.EmployeeNumber,
                WorkDate = a.WorkDate,
                ClockInUtc = a.ClockInUtc,
                ClockOutUtc = a.ClockOutUtc,
                Status = a.Status,
                Notes = a.Notes
            })
            .ToListAsync(ct);
    }

    public async Task<TodayAttendanceDto> GetTodayAsync(int employeeId, CancellationToken ct = default)
    {
        var workDate = await GetOrganizationDateAsync(ct);
        var record = await _db.AttendanceRecords.AsNoTracking()
            .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.WorkDate == workDate, ct);

        return new TodayAttendanceDto
        {
            WorkDate = workDate,
            ClockInUtc = record?.ClockInUtc,
            ClockOutUtc = record?.ClockOutUtc,
            Status = record?.Status,
            CanClockIn = record?.ClockInUtc is null,
            CanClockOut = record?.ClockInUtc is not null && record.ClockOutUtc is null
        };
    }

    public async Task ClockInAsync(int employeeId, CancellationToken ct = default)
    {
        var workDate = await GetOrganizationDateAsync(ct);
        var existing = await _db.AttendanceRecords
            .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.WorkDate == workDate, ct);

        if (existing is { ClockInUtc: not null })
            throw new ConflictException("You have already clocked in today.");

        var now = _clock.UtcNow;
        var status = await DetermineClockInStatusAsync(now, ct);

        if (existing is null)
        {
            _db.AttendanceRecords.Add(new AttendanceRecord
            {
                EmployeeId = employeeId,
                WorkDate = workDate,
                ClockInUtc = now,
                Status = status,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
        else
        {
            existing.ClockInUtc = now;
            existing.Status = status;
            existing.UpdatedAtUtc = now;
        }

        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(new AuditEntry
        {
            Action = "Attendance.ClockIn",
            TargetType = nameof(AttendanceRecord),
            TargetId = employeeId.ToString(),
            Metadata = $"Date={workDate};Status={status}"
        }, ct);
    }

    public async Task ClockOutAsync(int employeeId, CancellationToken ct = default)
    {
        var workDate = await GetOrganizationDateAsync(ct);
        var record = await _db.AttendanceRecords
            .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.WorkDate == workDate, ct)
            ?? throw new BusinessRuleException("You must clock in before clocking out.");

        if (record.ClockInUtc is null)
            throw new BusinessRuleException("You must clock in before clocking out.");
        if (record.ClockOutUtc is not null)
            throw new ConflictException("You have already clocked out today.");

        var now = _clock.UtcNow;
        if (now < record.ClockInUtc)
            throw new BusinessRuleException("Clock-out time cannot precede clock-in time.");

        record.ClockOutUtc = now;
        record.UpdatedAtUtc = now;
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(new AuditEntry
        {
            Action = "Attendance.ClockOut",
            TargetType = nameof(AttendanceRecord),
            TargetId = employeeId.ToString(),
            Metadata = $"Date={workDate}"
        }, ct);
    }

    public async Task<AttendanceListItemDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new AttendanceListItemDto
            {
                Id = a.Id,
                EmployeeId = a.EmployeeId,
                EmployeeName = a.Employee.FirstName + " " + a.Employee.LastName,
                EmployeeNumber = a.Employee.EmployeeNumber,
                DepartmentName = a.Employee.Department != null ? a.Employee.Department.Name : null,
                WorkDate = a.WorkDate,
                ClockInUtc = a.ClockInUtc,
                ClockOutUtc = a.ClockOutUtc,
                Status = a.Status,
                Notes = a.Notes
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> CorrectAsync(AttendanceCorrectionDto dto, int? recordId, int employeeId, CancellationToken ct = default)
    {
        if (dto.ClockIn.HasValue && dto.ClockOut.HasValue && dto.ClockOut < dto.ClockIn)
            throw new BusinessRuleException("Clock-out time cannot precede clock-in time.");

        AttendanceRecord? record = recordId.HasValue
            ? await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.Id == recordId, ct)
            : await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.WorkDate == dto.WorkDate, ct);

        var now = _clock.UtcNow;
        if (record is null)
        {
            record = new AttendanceRecord
            {
                EmployeeId = employeeId,
                WorkDate = dto.WorkDate,
                CreatedAtUtc = now
            };
            _db.AttendanceRecords.Add(record);
        }
        else
        {
            record.EmployeeId = employeeId;
            record.WorkDate = dto.WorkDate;
        }

        record.ClockInUtc = dto.ClockIn;
        record.ClockOutUtc = dto.ClockOut;
        record.Status = dto.Status;
        record.Notes = dto.Notes;
        record.CorrectedByUserId = _currentUser.UserId;
        record.CorrectedAtUtc = now;
        record.UpdatedAtUtc = now;

        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "Attendance.Corrected",
            TargetType = nameof(AttendanceRecord),
            TargetId = record.Id.ToString(),
            Metadata = $"EmployeeId={employeeId};Date={dto.WorkDate};Status={dto.Status}"
        }, ct);

        return record.Id;
    }

    public async Task<AttendanceSummaryDto> GetSummaryAsync(int employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var records = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.WorkDate >= from && a.WorkDate <= to)
            .ToListAsync(ct);

        return new AttendanceSummaryDto
        {
            PresentDays = records.Count(r => r.Status == AttendanceStatus.Present),
            LateDays = records.Count(r => r.Status == AttendanceStatus.Late),
            TotalWorkedHours = Math.Round(records
                .Where(r => r.ClockInUtc.HasValue && r.ClockOutUtc.HasValue)
                .Sum(r => (r.ClockOutUtc!.Value - r.ClockInUtc!.Value).TotalHours), 2)
        };
    }

    private async Task<DateOnly> GetOrganizationDateAsync(CancellationToken ct)
    {
        var tz = await _settings.GetOrganizationTimeZoneAsync(ct);
        var local = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, tz);
        return DateOnly.FromDateTime(local);
    }

    private async Task<AttendanceStatus> DetermineClockInStatusAsync(DateTime clockInUtc, CancellationToken ct)
    {
        var tz = await _settings.GetOrganizationTimeZoneAsync(ct);
        var local = TimeZoneInfo.ConvertTimeFromUtc(clockInUtc, tz);
        var startSetting = await _settings.GetAsync("Attendance.WorkDayStart", "09:00", ct);
        if (TimeOnly.TryParse(startSetting, out var start) && TimeOnly.FromDateTime(local) > start)
        {
            return AttendanceStatus.Late;
        }
        return AttendanceStatus.Present;
    }
}
