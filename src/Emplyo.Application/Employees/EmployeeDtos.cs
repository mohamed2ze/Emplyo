using System.ComponentModel.DataAnnotations;
using Emplyo.Domain.Enums;

namespace Emplyo.Application.Employees;

public class EmployeeFilter
{
    public string? Search { get; set; }
    public int? DepartmentId { get; set; }
    public int? PositionId { get; set; }
    public EmploymentStatus? Status { get; set; }
    public EmploymentType? EmploymentType { get; set; }
    public string? SortBy { get; set; }
    public string? SortDir { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 15;
}

public class EmployeeListItemDto
{
    public int Id { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string WorkEmail { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string? PositionTitle { get; set; }
    public EmploymentStatus Status { get; set; }
    public DateOnly HireDate { get; set; }
    public string? ProfilePhotoPath { get; set; }
    public string Initials => string.Concat(
        FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));
}

public class EmployeeDetailDto
{
    public int Id { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string WorkEmail { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? ProfilePhotoPath { get; set; }
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public int? PositionId { get; set; }
    public string? PositionTitle { get; set; }
    public int? ManagerId { get; set; }
    public string? ManagerName { get; set; }
    public EmploymentType EmploymentType { get; set; }
    public EmploymentStatus EmploymentStatus { get; set; }
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
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class EmployeeFormDto
{
    [Required, StringLength(50)]
    public string EmployeeNumber { get; set; } = string.Empty;

    [Required, StringLength(100)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    [Display(Name = "Work email")]
    public string WorkEmail { get; set; } = string.Empty;

    [Phone, StringLength(30)]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }

    [Display(Name = "Department")]
    public int? DepartmentId { get; set; }

    [Display(Name = "Position")]
    public int? PositionId { get; set; }

    [Display(Name = "Manager")]
    public int? ManagerId { get; set; }

    [Display(Name = "Employment type")]
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;

    [Display(Name = "Employment status")]
    public EmploymentStatus EmploymentStatus { get; set; } = EmploymentStatus.Pending;

    [Required, DataType(DataType.Date)]
    [Display(Name = "Hire date")]
    public DateOnly HireDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [Display(Name = "Work location")]
    public string? WorkLocation { get; set; }

    public string? AddressLine { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }

    [Display(Name = "Emergency contact")]
    public string? EmergencyContactName { get; set; }

    [Display(Name = "Emergency phone")]
    public string? EmergencyContactPhone { get; set; }

    [Display(Name = "Annual compensation")]
    [Range(0, 100_000_000)]
    public decimal? AnnualCompensation { get; set; }

    [Display(Name = "Currency"), StringLength(3)]
    public string? CompensationCurrency { get; set; } = "USD";
}

public class ManagerOptionDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
}
