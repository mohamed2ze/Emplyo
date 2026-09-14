using Emplyo.Domain.Enums;

namespace Emplyo.Domain.Entities;

public class OffboardingCase
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateOnly DepartureDate { get; set; }
    public string? Reason { get; set; }
    public OffboardingStatus Status { get; set; } = OffboardingStatus.Initiated;
    public string InitiatedByUserId { get; set; } = string.Empty;
    public DateTime InitiatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<OffboardingTask> Tasks { get; set; } = new List<OffboardingTask>();
}

public class OffboardingTask
{
    public int Id { get; set; }
    public int OffboardingCaseId { get; set; }
    public OffboardingCase OffboardingCase { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly? DueDate { get; set; }
    public TaskItemStatus Status { get; set; } = TaskItemStatus.Pending;
    public DateTime? CompletedAtUtc { get; set; }
    public string? CompletedByUserId { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
