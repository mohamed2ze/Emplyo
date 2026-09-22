using Emplyo.Domain.Enums;

namespace Emplyo.Application.Reporting;

public class DepartmentHeadcountDto
{
    public string DepartmentName { get; set; } = string.Empty;
    public int ActiveCount { get; set; }
    public int TotalCount { get; set; }
}

public class StatusSummaryDto
{
    public EmploymentStatus Status { get; set; }
    public int Count { get; set; }
}

public class LeaveUtilizationDto
{
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string LeaveTypeName { get; set; } = string.Empty;
    public int Year { get; set; }
    public decimal Entitlement { get; set; }
    public decimal Used { get; set; }
    public decimal Remaining { get; set; }
}

public class AttendanceReportRowDto
{
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public int PresentDays { get; set; }
    public int LateDays { get; set; }
    public int AbsentDays { get; set; }
    public int RemoteDays { get; set; }
    public double TotalHours { get; set; }
}

public class RecentHireDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string? PositionTitle { get; set; }
    public DateOnly HireDate { get; set; }
    public EmploymentStatus Status { get; set; }
}

public class EmployeeDirectoryRowDto
{
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string WorkEmail { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string? PositionTitle { get; set; }
    public string? ManagerName { get; set; }
    public EmploymentType EmploymentType { get; set; }
    public EmploymentStatus Status { get; set; }
    public DateOnly HireDate { get; set; }
    public string? WorkLocation { get; set; }
}

public interface IReportService
{
    Task<IReadOnlyList<DepartmentHeadcountDto>> GetHeadcountByDepartmentAsync(CancellationToken ct = default);
    Task<IReadOnlyList<StatusSummaryDto>> GetStatusSummaryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LeaveUtilizationDto>> GetLeaveUtilizationAsync(int? year, int? departmentId, CancellationToken ct = default);
    Task<IReadOnlyList<AttendanceReportRowDto>> GetAttendanceReportAsync(DateOnly from, DateOnly to, int? departmentId, CancellationToken ct = default);
    Task<IReadOnlyList<RecentHireDto>> GetRecentHiresAsync(int count, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeDirectoryRowDto>> GetEmployeeDirectoryAsync(int? departmentId, EmploymentStatus? status, CancellationToken ct = default);
}
