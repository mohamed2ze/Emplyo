using Emplyo.Domain.Enums;

namespace Emplyo.Domain.Entities;

public class Employee
{
    public int Id { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string WorkEmail { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? ProfilePhotoPath { get; set; }

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public int? PositionId { get; set; }
    public Position? Position { get; set; }

    public int? ManagerId { get; set; }
    public Employee? Manager { get; set; }

    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;
    public EmploymentStatus EmploymentStatus { get; set; } = EmploymentStatus.Pending;
    public DateOnly HireDate { get; set; }
    public DateOnly? TerminationDate { get; set; }
    public string? WorkLocation { get; set; }

    public string? AddressLine { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }

    public decimal? AnnualCompensation { get; set; }
    public string? CompensationCurrency { get; set; }

    public string? UserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public byte[]? RowVersion { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();

    public ICollection<Employee> DirectReports { get; set; } = new List<Employee>();
    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
    public ICollection<LeaveRequest> LeaveRequests { get; set; } = new List<LeaveRequest>();
    public ICollection<LeaveBalance> LeaveBalances { get; set; } = new List<LeaveBalance>();
    public ICollection<EmployeeDocument> Documents { get; set; } = new List<EmployeeDocument>();
    public ICollection<OnboardingTask> OnboardingTasks { get; set; } = new List<OnboardingTask>();
    public ICollection<OffboardingCase> OffboardingCases { get; set; } = new List<OffboardingCase>();
}
