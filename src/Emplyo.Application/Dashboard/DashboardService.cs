using Emplyo.Application.Attendance;
using Emplyo.Application.Auditing;
using Emplyo.Application.Announcements;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.LeaveManagement;
using Emplyo.Application.Reporting;
using Emplyo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Application.Dashboard;

public class DashboardService : IDashboardService
{
    private readonly IApplicationDbContext _db;
    private readonly IReportService _reports;
    private readonly IHolidayService _holidays;
    private readonly ILeaveService _leave;
    private readonly IAttendanceService _attendance;
    private readonly IAnnouncementService _announcements;
    private readonly IAppSettingsService _settings;
    private readonly IDateTimeProvider _clock;

    public DashboardService(IApplicationDbContext db, IReportService reports, IHolidayService holidays,
        ILeaveService leave, IAttendanceService attendance, IAnnouncementService announcements,
        IAppSettingsService settings, IDateTimeProvider clock)
    {
        _db = db;
        _reports = reports;
        _holidays = holidays;
        _leave = leave;
        _attendance = attendance;
        _announcements = announcements;
        _settings = settings;
        _clock = clock;
    }

    public async Task<AdminDashboardDto> GetAdminDashboardAsync(CancellationToken ct = default)
    {
        var statusSummary = await _reports.GetStatusSummaryAsync(ct);
        var recentActivity = await _db.AuditLogs.AsNoTracking()
            .OrderByDescending(a => a.TimestampUtc).Take(8)
            .Select(a => new AuditLogDto
            {
                Id = a.Id, EventId = a.EventId, ActorName = a.ActorName, Action = a.Action,
                TargetType = a.TargetType, TargetId = a.TargetId, Outcome = a.Outcome,
                Metadata = a.Metadata, TimestampUtc = a.TimestampUtc
            }).ToListAsync(ct);

        return new AdminDashboardDto
        {
            TotalEmployees = await _db.Employees.CountAsync(ct),
            ActiveEmployees = await _db.Employees.CountAsync(e => e.EmploymentStatus == EmploymentStatus.Active, ct),
            OnLeaveCount = await _db.Employees.CountAsync(e => e.EmploymentStatus == EmploymentStatus.OnLeave, ct),
            TerminatedCount = await _db.Employees.CountAsync(e => e.EmploymentStatus == EmploymentStatus.Terminated, ct),
            DepartmentCount = await _db.Departments.CountAsync(ct),
            PositionCount = await _db.Positions.CountAsync(ct),
            PendingLeaveRequests = await _leave.GetPendingCountAsync(null, ct),
            RecentHires = await _reports.GetRecentHiresAsync(5, ct),
            UpcomingHolidays = await _holidays.GetUpcomingAsync(4, ct),
            StatusSummary = statusSummary,
            HeadcountByDepartment = await _reports.GetHeadcountByDepartmentAsync(ct),
            RecentActivity = recentActivity
        };
    }

    public async Task<HrDashboardDto> GetHrDashboardAsync(CancellationToken ct = default)
    {
        var tz = await _settings.GetOrganizationTimeZoneAsync(ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, tz));
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var todayRecords = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.WorkDate == today)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var milestones = await GetUpcomingMilestonesAsync(today, ct);

        return new HrDashboardDto
        {
            Headcount = await _db.Employees.CountAsync(e => e.EmploymentStatus != EmploymentStatus.Terminated, ct),
            ActiveEmployees = await _db.Employees.CountAsync(e => e.EmploymentStatus == EmploymentStatus.Active, ct),
            PendingLeaveRequests = await _leave.GetPendingCountAsync(null, ct),
            NewHiresThisMonth = await _db.Employees.CountAsync(e => e.HireDate >= monthStart, ct),
            OnboardingInProgress = await _db.Employees.CountAsync(e => e.OnboardingTasks.Any(), ct),
            PresentToday = todayRecords.FirstOrDefault(r => r.Status == AttendanceStatus.Present)?.Count ?? 0,
            LateToday = todayRecords.FirstOrDefault(r => r.Status == AttendanceStatus.Late)?.Count ?? 0,
            AbsentToday = todayRecords.FirstOrDefault(r => r.Status == AttendanceStatus.Absent)?.Count ?? 0,
            RecentHires = await _reports.GetRecentHiresAsync(5, ct),
            UpcomingHolidays = await _holidays.GetUpcomingAsync(4, ct),
            HeadcountByDepartment = await _reports.GetHeadcountByDepartmentAsync(ct),
            UpcomingMilestones = milestones
        };
    }

    public async Task<EmployeeDashboardDto?> GetEmployeeDashboardAsync(int employeeId, IReadOnlyCollection<string> roles, CancellationToken ct = default)
    {
        var employee = await _db.Employees.AsNoTracking()
            .Include(e => e.Department).Include(e => e.Position)
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return null;

        var tz = await _settings.GetOrganizationTimeZoneAsync(ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, tz));

        var leaveRequests = await _leave.GetPagedAsync(new LeaveFilter
        {
            EmployeeId = employeeId, Page = 1, PageSize = 5
        }, ct);

        return new EmployeeDashboardDto
        {
            EmployeeId = employee.Id,
            EmployeeName = employee.FirstName + " " + employee.LastName,
            DepartmentName = employee.Department?.Name,
            PositionTitle = employee.Position?.Title,
            EmployeeNumber = employee.EmployeeNumber,
            HireDate = employee.HireDate,
            EmploymentStatus = employee.EmploymentStatus,
            ProfilePhotoPath = employee.ProfilePhotoPath,
            LeaveBalances = await _leave.GetBalancesAsync(employeeId, null, ct),
            RecentAttendance = await _attendance.GetForEmployeeAsync(employeeId, today.AddDays(-13), today, ct),
            Today = await _attendance.GetTodayAsync(employeeId, ct),
            RecentLeaveRequests = leaveRequests.Items,
            Announcements = await _announcements.GetVisibleForUserAsync(roles, employee.DepartmentId, 5, ct),
            UpcomingHolidays = await _holidays.GetUpcomingAsync(4, ct)
        };
    }

    private async Task<IReadOnlyList<EmployeeMilestoneDto>> GetUpcomingMilestonesAsync(DateOnly today, CancellationToken ct)
    {
        var horizon = today.AddDays(45);
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.EmploymentStatus == EmploymentStatus.Active)
            .Select(e => new { e.Id, Name = e.FirstName + " " + e.LastName, e.HireDate })
            .ToListAsync(ct);

        var milestones = new List<EmployeeMilestoneDto>();
        foreach (var e in employees)
        {
            var anniversary = new DateOnly(today.Year, e.HireDate.Month, Math.Min(e.HireDate.Day, DateTime.DaysInMonth(today.Year, e.HireDate.Month)));
            if (anniversary < today) anniversary = anniversary.AddYears(1);
            if (anniversary >= today && anniversary <= horizon && anniversary.Year > e.HireDate.Year)
            {
                milestones.Add(new EmployeeMilestoneDto
                {
                    EmployeeId = e.Id,
                    EmployeeName = e.Name,
                    MilestoneType = "Work anniversary",
                    Date = anniversary,
                    Years = anniversary.Year - e.HireDate.Year
                });
            }
        }

        return milestones.OrderBy(m => m.Date).Take(6).ToList();
    }
}
