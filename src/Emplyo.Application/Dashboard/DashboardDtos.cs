using Emplyo.Application.Attendance;
using Emplyo.Application.Auditing;
using Emplyo.Application.LeaveManagement;

namespace Emplyo.Application.Dashboard;

public class EmployeeMilestoneDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string MilestoneType { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public int Years { get; set; }
}

public class AdminDashboardDto
{
    public int TotalEmployees { get; set; }
    public int ActiveEmployees { get; set; }
    public int OnLeaveCount { get; set; }
    public int TerminatedCount { get; set; }
    public int DepartmentCount { get; set; }
    public int PositionCount { get; set; }
    public int PendingLeaveRequests { get; set; }
    public IReadOnlyList<Reporting.RecentHireDto> RecentHires { get; set; } = Array.Empty<Reporting.RecentHireDto>();
    public IReadOnlyList<HolidayDto> UpcomingHolidays { get; set; } = Array.Empty<HolidayDto>();
    public IReadOnlyList<Reporting.StatusSummaryDto> StatusSummary { get; set; } = Array.Empty<Reporting.StatusSummaryDto>();
    public IReadOnlyList<Reporting.DepartmentHeadcountDto> HeadcountByDepartment { get; set; } = Array.Empty<Reporting.DepartmentHeadcountDto>();
    public IReadOnlyList<AuditLogDto> RecentActivity { get; set; } = Array.Empty<AuditLogDto>();
}

public class HrDashboardDto
{
    public int Headcount { get; set; }
    public int ActiveEmployees { get; set; }
    public int PendingLeaveRequests { get; set; }
    public int NewHiresThisMonth { get; set; }
    public int OnboardingInProgress { get; set; }
    public int PresentToday { get; set; }
    public int LateToday { get; set; }
    public int AbsentToday { get; set; }
    public IReadOnlyList<Reporting.RecentHireDto> RecentHires { get; set; } = Array.Empty<Reporting.RecentHireDto>();
    public IReadOnlyList<HolidayDto> UpcomingHolidays { get; set; } = Array.Empty<HolidayDto>();
    public IReadOnlyList<Reporting.DepartmentHeadcountDto> HeadcountByDepartment { get; set; } = Array.Empty<Reporting.DepartmentHeadcountDto>();
    public IReadOnlyList<EmployeeMilestoneDto> UpcomingMilestones { get; set; } = Array.Empty<EmployeeMilestoneDto>();
}

public class EmployeeDashboardDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string? PositionTitle { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public DateOnly HireDate { get; set; }
    public Domain.Enums.EmploymentStatus EmploymentStatus { get; set; }
    public string? ProfilePhotoPath { get; set; }
    public IReadOnlyList<LeaveBalanceDto> LeaveBalances { get; set; } = Array.Empty<LeaveBalanceDto>();
    public IReadOnlyList<AttendanceListItemDto> RecentAttendance { get; set; } = Array.Empty<AttendanceListItemDto>();
    public Attendance.TodayAttendanceDto? Today { get; set; }
    public IReadOnlyList<LeaveRequestListItemDto> RecentLeaveRequests { get; set; } = Array.Empty<LeaveRequestListItemDto>();
    public IReadOnlyList<Announcements.AnnouncementListItemDto> Announcements { get; set; } = Array.Empty<Announcements.AnnouncementListItemDto>();
    public IReadOnlyList<HolidayDto> UpcomingHolidays { get; set; } = Array.Empty<HolidayDto>();
}

public interface IDashboardService
{
    Task<AdminDashboardDto> GetAdminDashboardAsync(CancellationToken ct = default);
    Task<HrDashboardDto> GetHrDashboardAsync(CancellationToken ct = default);
    Task<EmployeeDashboardDto?> GetEmployeeDashboardAsync(int employeeId, IReadOnlyCollection<string> roles, CancellationToken ct = default);
}
