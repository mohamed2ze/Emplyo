using Emplyo.Application.Employees;
using Emplyo.Application.Organization;

namespace Emplyo.Web.ViewModels;

public class EmployeeFormViewModel
{
    public EmployeeFormDto Form { get; set; } = new();
    public IReadOnlyList<LookupItemDto> Departments { get; set; } = Array.Empty<LookupItemDto>();
    public IReadOnlyList<LookupItemDto> Positions { get; set; } = Array.Empty<LookupItemDto>();
    public IReadOnlyList<ManagerOptionDto> Managers { get; set; } = Array.Empty<ManagerOptionDto>();
    public bool IsEdit { get; set; }
    public int Id { get; set; }
    public string? ProfilePhotoPath { get; set; }
    public string? EmployeeName { get; set; }
}

public class EmployeeDetailsViewModel
{
    public EmployeeDetailDto Employee { get; set; } = new();
    public bool CanViewSensitive { get; set; }
    public bool CanEdit { get; set; }
    public bool IsSelf { get; set; }
    public IReadOnlyList<Emplyo.Application.Attendance.AttendanceListItemDto> RecentAttendance { get; set; } = Array.Empty<Emplyo.Application.Attendance.AttendanceListItemDto>();
    public IReadOnlyList<Emplyo.Application.LeaveManagement.LeaveRequestListItemDto> RecentLeave { get; set; } = Array.Empty<Emplyo.Application.LeaveManagement.LeaveRequestListItemDto>();
    public IReadOnlyList<Emplyo.Application.Documents.DocumentListItemDto> Documents { get; set; } = Array.Empty<Emplyo.Application.Documents.DocumentListItemDto>();
}

public class DepartmentFormViewModel
{
    public DepartmentFormDto Form { get; set; } = new();
    public bool IsEdit { get; set; }
    public int Id { get; set; }
    public IReadOnlyList<ManagerOptionDto> Managers { get; set; } = Array.Empty<ManagerOptionDto>();
}

public class PositionFormViewModel
{
    public PositionFormDto Form { get; set; } = new();
    public bool IsEdit { get; set; }
    public int Id { get; set; }
    public IReadOnlyList<LookupItemDto> Departments { get; set; } = Array.Empty<LookupItemDto>();
}
