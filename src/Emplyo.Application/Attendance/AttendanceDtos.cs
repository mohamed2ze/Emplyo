using System.ComponentModel.DataAnnotations;
using Emplyo.Application.Common.Models;
using Emplyo.Domain.Enums;

namespace Emplyo.Application.Attendance;

public class AttendanceFilter
{
    public int? EmployeeId { get; set; }
    public string? Search { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public AttendanceStatus? Status { get; set; }
    public string? SortBy { get; set; }
    public string? SortDir { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 15;
}

public class AttendanceListItemDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public DateOnly WorkDate { get; set; }
    public DateTime? ClockInUtc { get; set; }
    public DateTime? ClockOutUtc { get; set; }
    public AttendanceStatus Status { get; set; }
    public string? Notes { get; set; }
    public double? WorkedHours => ClockInUtc.HasValue && ClockOutUtc.HasValue
        ? Math.Round((ClockOutUtc.Value - ClockInUtc.Value).TotalHours, 2) : null;
}

public class AttendanceCorrectionDto
{
    [Required, DataType(DataType.Date)]
    [Display(Name = "Work date")]
    public DateOnly WorkDate { get; set; }

    [Display(Name = "Clock in")]
    public DateTime? ClockIn { get; set; }

    [Display(Name = "Clock out")]
    public DateTime? ClockOut { get; set; }

    [Display(Name = "Status")]
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;

    [StringLength(500)]
    public string? Notes { get; set; }
}

public class AttendanceSummaryDto
{
    public int PresentDays { get; set; }
    public int LateDays { get; set; }
    public double TotalWorkedHours { get; set; }
}

public class TodayAttendanceDto
{
    public DateOnly WorkDate { get; set; }
    public DateTime? ClockInUtc { get; set; }
    public DateTime? ClockOutUtc { get; set; }
    public AttendanceStatus? Status { get; set; }
    public bool CanClockIn { get; set; }
    public bool CanClockOut { get; set; }
}

public interface IAttendanceService
{
    Task<PagedResult<AttendanceListItemDto>> GetPagedAsync(AttendanceFilter filter, CancellationToken ct = default);
    Task<IReadOnlyList<AttendanceListItemDto>> GetForEmployeeAsync(int employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<TodayAttendanceDto> GetTodayAsync(int employeeId, CancellationToken ct = default);
    Task ClockInAsync(int employeeId, CancellationToken ct = default);
    Task ClockOutAsync(int employeeId, CancellationToken ct = default);
    Task<AttendanceListItemDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<int> CorrectAsync(AttendanceCorrectionDto dto, int? recordId, int employeeId, CancellationToken ct = default);
    Task<AttendanceSummaryDto> GetSummaryAsync(int employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
