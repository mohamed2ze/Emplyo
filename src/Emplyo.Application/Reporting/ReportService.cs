using Emplyo.Application.Common.Interfaces;
using Emplyo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Application.Reporting;

public class ReportService : IReportService
{
    private readonly IApplicationDbContext _db;

    public ReportService(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<DepartmentHeadcountDto>> GetHeadcountByDepartmentAsync(CancellationToken ct = default)
    {
        var departments = await _db.Departments.AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentHeadcountDto
            {
                DepartmentName = d.Name,
                ActiveCount = d.Employees.Count(e => e.EmploymentStatus == EmploymentStatus.Active),
                TotalCount = d.Employees.Count(e => e.EmploymentStatus != EmploymentStatus.Terminated)
            }).ToListAsync(ct);

        var unassigned = await _db.Employees.AsNoTracking()
            .Where(e => e.DepartmentId == null && e.EmploymentStatus != EmploymentStatus.Terminated)
            .CountAsync(ct);
        if (unassigned > 0)
        {
            departments.Add(new DepartmentHeadcountDto { DepartmentName = "Unassigned", ActiveCount = unassigned, TotalCount = unassigned });
        }

        return departments;
    }

    public async Task<IReadOnlyList<StatusSummaryDto>> GetStatusSummaryAsync(CancellationToken ct = default)
    {
        var grouped = await _db.Employees.AsNoTracking()
            .GroupBy(e => e.EmploymentStatus)
            .Select(g => new StatusSummaryDto { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return Enum.GetValues<EmploymentStatus>()
            .Select(s => new StatusSummaryDto { Status = s, Count = grouped.FirstOrDefault(g => g.Status == s)?.Count ?? 0 })
            .ToList();
    }

    public async Task<IReadOnlyList<LeaveUtilizationDto>> GetLeaveUtilizationAsync(int? year, int? departmentId, CancellationToken ct = default)
    {
        var y = year ?? DateTime.UtcNow.Year;
        var query = _db.LeaveBalances.AsNoTracking()
            .Include(b => b.Employee).ThenInclude(e => e.Department)
            .Include(b => b.LeaveType)
            .Where(b => b.Year == y);

        if (departmentId.HasValue)
            query = query.Where(b => b.Employee.DepartmentId == departmentId);

        return await query
            .OrderBy(b => b.Employee.FirstName).ThenBy(b => b.LeaveType.Name)
            .Select(b => new LeaveUtilizationDto
            {
                EmployeeName = b.Employee.FirstName + " " + b.Employee.LastName,
                EmployeeNumber = b.Employee.EmployeeNumber,
                DepartmentName = b.Employee.Department != null ? b.Employee.Department.Name : null,
                LeaveTypeName = b.LeaveType.Name,
                Year = b.Year,
                Entitlement = b.Entitlement,
                Used = b.Used,
                Remaining = b.Entitlement - b.Used
            }).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AttendanceReportRowDto>> GetAttendanceReportAsync(DateOnly from, DateOnly to, int? departmentId, CancellationToken ct = default)
    {
        var query = _db.AttendanceRecords.AsNoTracking()
            .Include(a => a.Employee).ThenInclude(e => e.Department)
            .Where(a => a.WorkDate >= from && a.WorkDate <= to);

        if (departmentId.HasValue)
            query = query.Where(a => a.Employee.DepartmentId == departmentId);

        var grouped = await query
            .GroupBy(a => new
            {
                a.EmployeeId,
                Name = a.Employee.FirstName + " " + a.Employee.LastName,
                a.Employee.EmployeeNumber,
                Department = a.Employee.Department != null ? a.Employee.Department.Name : null
            })
            .Select(g => new AttendanceReportRowDto
            {
                EmployeeName = g.Key.Name,
                EmployeeNumber = g.Key.EmployeeNumber,
                DepartmentName = g.Key.Department,
                PresentDays = g.Count(a => a.Status == AttendanceStatus.Present),
                LateDays = g.Count(a => a.Status == AttendanceStatus.Late),
                AbsentDays = g.Count(a => a.Status == AttendanceStatus.Absent),
                RemoteDays = g.Count(a => a.Status == AttendanceStatus.Remote),
                TotalHours = g.Where(a => a.ClockInUtc != null && a.ClockOutUtc != null)
                    .Sum(a => (a.ClockOutUtc!.Value - a.ClockInUtc!.Value).TotalHours)
            })
            .ToListAsync(ct);

        return grouped.Select(r => new AttendanceReportRowDto
        {
            EmployeeName = r.EmployeeName,
            EmployeeNumber = r.EmployeeNumber,
            DepartmentName = r.DepartmentName,
            PresentDays = r.PresentDays,
            LateDays = r.LateDays,
            AbsentDays = r.AbsentDays,
            RemoteDays = r.RemoteDays,
            TotalHours = Math.Round(r.TotalHours, 2)
        }).ToList();
    }

    public async Task<IReadOnlyList<RecentHireDto>> GetRecentHiresAsync(int count, CancellationToken ct = default)
    {
        return await _db.Employees.AsNoTracking()
            .Include(e => e.Department).Include(e => e.Position)
            .OrderByDescending(e => e.HireDate)
            .Take(count)
            .Select(e => new RecentHireDto
            {
                EmployeeId = e.Id,
                EmployeeName = e.FirstName + " " + e.LastName,
                DepartmentName = e.Department != null ? e.Department.Name : null,
                PositionTitle = e.Position != null ? e.Position.Title : null,
                HireDate = e.HireDate,
                Status = e.EmploymentStatus
            }).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EmployeeDirectoryRowDto>> GetEmployeeDirectoryAsync(int? departmentId, EmploymentStatus? status, CancellationToken ct = default)
    {
        var query = _db.Employees.AsNoTracking()
            .Include(e => e.Department).Include(e => e.Position).Include(e => e.Manager)
            .AsQueryable();

        if (departmentId.HasValue) query = query.Where(e => e.DepartmentId == departmentId);
        if (status.HasValue) query = query.Where(e => e.EmploymentStatus == status);

        return await query.OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
            .Select(e => new EmployeeDirectoryRowDto
            {
                EmployeeNumber = e.EmployeeNumber,
                FullName = e.FirstName + " " + e.LastName,
                WorkEmail = e.WorkEmail,
                DepartmentName = e.Department != null ? e.Department.Name : null,
                PositionTitle = e.Position != null ? e.Position.Title : null,
                ManagerName = e.Manager != null ? e.Manager.FirstName + " " + e.Manager.LastName : null,
                EmploymentType = e.EmploymentType,
                Status = e.EmploymentStatus,
                HireDate = e.HireDate,
                WorkLocation = e.WorkLocation
            }).ToListAsync(ct);
    }
}
