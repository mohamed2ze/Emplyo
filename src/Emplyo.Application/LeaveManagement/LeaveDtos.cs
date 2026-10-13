using System.ComponentModel.DataAnnotations;
using Emplyo.Application.Common.Models;
using Emplyo.Domain.Enums;

namespace Emplyo.Application.LeaveManagement;

public class LeaveFilter
{
    public int? EmployeeId { get; set; }
    public int? LeaveTypeId { get; set; }
    public LeaveRequestStatus? Status { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public string? SortDir { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 15;
}

public class LeaveRequestListItemDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string LeaveTypeName { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal TotalDays { get; set; }
    public LeaveRequestStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class LeaveRequestDetailDto : LeaveRequestListItemDto
{
    public bool IsHalfDay { get; set; }
    public string? Reason { get; set; }
    public string? DecisionByName { get; set; }
    public DateTime? DecisionAtUtc { get; set; }
    public string? DecisionComments { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class LeaveRequestFormDto
{
    [Display(Name = "Leave type")]
    public int LeaveTypeId { get; set; }

    [Required, DataType(DataType.Date)]
    [Display(Name = "Start date")]
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [Required, DataType(DataType.Date)]
    [Display(Name = "End date")]
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [Display(Name = "Half day")]
    public bool IsHalfDay { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }
}

public class LeaveDecisionDto
{
    [StringLength(500)]
    [Display(Name = "Comment")]
    public string? Comments { get; set; }
}

public class LeaveBalanceDto
{
    public int LeaveTypeId { get; set; }
    public string LeaveTypeName { get; set; } = string.Empty;
    public decimal Entitlement { get; set; }
    public decimal Used { get; set; }
    public decimal Remaining { get; set; }
    public int Year { get; set; }
}

public class LeaveTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsPaid { get; set; }
    public decimal DefaultAnnualEntitlement { get; set; }
    public bool RequiresApproval { get; set; }
    public bool IsActive { get; set; }
    public int RequestCount { get; set; }
}

public class LeaveTypeFormDto
{
    [Required, StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [StringLength(400)]
    public string? Description { get; set; }

    [Display(Name = "Paid")]
    public bool IsPaid { get; set; } = true;

    [Range(0, 365)]
    [Display(Name = "Default annual entitlement (days)")]
    public decimal DefaultAnnualEntitlement { get; set; }

    [Display(Name = "Requires approval")]
    public bool RequiresApproval { get; set; } = true;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public class HolidayDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string? Description { get; set; }
    public bool IsRecurringAnnually { get; set; }
}

public class HolidayFormDto
{
    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, DataType(DataType.Date)]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [StringLength(300)]
    public string? Description { get; set; }

    [Display(Name = "Repeats annually")]
    public bool IsRecurringAnnually { get; set; }
}

public interface ILeaveService
{
    Task<PagedResult<LeaveRequestListItemDto>> GetPagedAsync(LeaveFilter filter, CancellationToken ct = default);
    Task<LeaveRequestDetailDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<int> SubmitAsync(int employeeId, LeaveRequestFormDto dto, CancellationToken ct = default);
    Task ApproveAsync(int id, LeaveDecisionDto decision, CancellationToken ct = default);
    Task RejectAsync(int id, LeaveDecisionDto decision, CancellationToken ct = default);
    Task CancelAsync(int id, int? actingEmployeeId, bool isPrivileged, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveBalanceDto>> GetBalancesAsync(int employeeId, int? year = null, CancellationToken ct = default);
    Task<int> GetPendingCountAsync(int? employeeId = null, CancellationToken ct = default);
}

public interface ILeaveTypeService
{
    Task<IReadOnlyList<LeaveTypeDto>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<LeaveTypeFormDto?> GetFormAsync(int id, CancellationToken ct = default);
    Task<int> CreateAsync(LeaveTypeFormDto dto, CancellationToken ct = default);
    Task UpdateAsync(int id, LeaveTypeFormDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<Domain.Entities.LeaveType>> GetActiveEntitiesAsync(CancellationToken ct = default);
}

public interface IHolidayService
{
    Task<IReadOnlyList<HolidayDto>> GetAsync(int? year = null, CancellationToken ct = default);
    Task<IReadOnlyList<HolidayDto>> GetUpcomingAsync(int count, CancellationToken ct = default);
    Task<HolidayFormDto?> GetFormAsync(int id, CancellationToken ct = default);
    Task<int> CreateAsync(HolidayFormDto dto, CancellationToken ct = default);
    Task UpdateAsync(int id, HolidayFormDto dto, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
