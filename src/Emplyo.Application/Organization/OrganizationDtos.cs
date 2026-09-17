using System.ComponentModel.DataAnnotations;
using Emplyo.Application.Common.Models;

namespace Emplyo.Application.Organization;

public class DepartmentListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DepartmentHeadName { get; set; }
    public bool IsActive { get; set; }
    public int EmployeeCount { get; set; }
    public int PositionCount { get; set; }
}

public class DepartmentDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? DepartmentHeadId { get; set; }
    public string? DepartmentHeadName { get; set; }
    public bool IsActive { get; set; }
    public int EmployeeCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<EmployeeSummaryDto> Employees { get; set; } = new();
    public List<PositionListItemDto> Positions { get; set; } = new();
}

public class EmployeeSummaryDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? PositionTitle { get; set; }
    public Domain.Enums.EmploymentStatus Status { get; set; }
    public string? ProfilePhotoPath { get; set; }
}

public class DepartmentFormDto
{
    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Display(Name = "Department head")]
    public int? DepartmentHeadId { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public class PositionListItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public bool IsActive { get; set; }
    public int EmployeeCount { get; set; }
}

public class PositionFormDto
{
    [Required, StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Display(Name = "Department")]
    public int? DepartmentId { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public class LookupItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public interface IDepartmentService
{
    Task<PagedResult<DepartmentListItemDto>> GetPagedAsync(string? search, bool? active, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItemDto>> GetLookupAsync(bool activeOnly = true, CancellationToken ct = default);
    Task<DepartmentDetailDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<int> CreateAsync(DepartmentFormDto dto, CancellationToken ct = default);
    Task UpdateAsync(int id, DepartmentFormDto dto, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, int? excludeId = null, CancellationToken ct = default);
}

public interface IPositionService
{
    Task<PagedResult<PositionListItemDto>> GetPagedAsync(string? search, int? departmentId, bool? active, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItemDto>> GetLookupAsync(int? departmentId = null, bool activeOnly = true, CancellationToken ct = default);
    Task<PositionListItemDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<int> CreateAsync(PositionFormDto dto, CancellationToken ct = default);
    Task UpdateAsync(int id, PositionFormDto dto, CancellationToken ct = default);
    Task<bool> TitleExistsAsync(string title, int? departmentId, int? excludeId = null, CancellationToken ct = default);
}
