using System.ComponentModel.DataAnnotations;
using Emplyo.Application.Common.Models;
using Emplyo.Domain.Enums;

namespace Emplyo.Application.Lifecycle;

public class OnboardingOverviewDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string? PositionTitle { get; set; }
    public DateOnly HireDate { get; set; }
    public EmploymentStatus EmploymentStatus { get; set; }
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int ProgressPercent => TotalTasks == 0 ? 0 : (int)Math.Round(CompletedTasks * 100.0 / TotalTasks);
}

public class TaskDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly? DueDate { get; set; }
    public TaskItemStatus Status { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int SortOrder { get; set; }
}

public class OnboardingDetailDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string? PositionTitle { get; set; }
    public DateOnly HireDate { get; set; }
    public List<TaskDto> Tasks { get; set; } = new();
}

public class TaskFormDto
{
    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Due date")]
    public DateOnly? DueDate { get; set; }

    [Display(Name = "Order")]
    public int SortOrder { get; set; }
}

public class OffboardingOverviewDto
{
    public int CaseId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public DateOnly DepartureDate { get; set; }
    public string? Reason { get; set; }
    public OffboardingStatus Status { get; set; }
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public DateTime InitiatedAtUtc { get; set; }
    public int ProgressPercent => TotalTasks == 0 ? 0 : (int)Math.Round(CompletedTasks * 100.0 / TotalTasks);
}

public class OffboardingDetailDto
{
    public int CaseId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public DateOnly DepartureDate { get; set; }
    public string? Reason { get; set; }
    public OffboardingStatus Status { get; set; }
    public DateTime InitiatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public List<TaskDto> Tasks { get; set; } = new();
}

public class OffboardingInitiateDto
{
    [Required, DataType(DataType.Date)]
    [Display(Name = "Departure date")]
    public DateOnly DepartureDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [StringLength(500)]
    public string? Reason { get; set; }
}

public interface ILifecycleService
{
    Task<PagedResult<OnboardingOverviewDto>> GetOnboardingAsync(string? search, int page, int pageSize, CancellationToken ct = default);
    Task<OnboardingDetailDto?> GetOnboardingDetailAsync(int employeeId, CancellationToken ct = default);
    Task AddOnboardingTaskAsync(int employeeId, TaskFormDto dto, CancellationToken ct = default);
    Task ApplyOnboardingTemplateAsync(int employeeId, CancellationToken ct = default);
    Task SetOnboardingTaskStatusAsync(int taskId, TaskItemStatus status, CancellationToken ct = default);
    Task DeleteOnboardingTaskAsync(int taskId, CancellationToken ct = default);

    Task<PagedResult<OffboardingOverviewDto>> GetOffboardingAsync(string? search, int page, int pageSize, CancellationToken ct = default);
    Task<OffboardingDetailDto?> GetOffboardingDetailAsync(int caseId, CancellationToken ct = default);
    Task<int> InitiateOffboardingAsync(int employeeId, OffboardingInitiateDto dto, CancellationToken ct = default);
    Task SetOffboardingTaskStatusAsync(int taskId, TaskItemStatus status, CancellationToken ct = default);
    Task CompleteOffboardingAsync(int caseId, CancellationToken ct = default);
}
